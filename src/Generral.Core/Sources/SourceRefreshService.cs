using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Instrumentation;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Sources;

public sealed record SourceRefreshResult(int SourceId, string SourceName, bool Success, int Added, int Changed, int Unchanged, int Gone, string? Error)
{
    public string Summary => Success
        ? $"{SourceName}: {Added} added, {Changed} changed, {Unchanged} unchanged, {Gone} gone"
        : $"{SourceName}: {Error}";
}

/// <summary>Runs a source's type and upserts its events (docs/specs/04-sources.md § Fetch semantics).</summary>
public sealed class SourceRefreshService(IServiceScopeFactory scopes, SourceTypeRegistry types, TimeProvider time, ILogger<SourceRefreshService> logger)
{
    public const int MissedRefreshesUntilGone = 3;
    public const int MaxConcurrentRefreshes = 4;
    public static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RevisionRetention = TimeSpan.FromDays(30);

    private readonly SemaphoreSlim _global = new(MaxConcurrentRefreshes);
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _perSource = new();

    /// <summary>Null when the source was never refreshed (it is due at once).</summary>
    public static DateTime? NextRefresh(Source source) =>
        source.LastRefresh + (source.RefreshInterval < MinRefreshInterval ? MinRefreshInterval : source.RefreshInterval);

    public static bool IsDue(Source source, DateTime now) => source.Enabled && !(NextRefresh(source) > now);

    /// <param name="onlyIfDue">Skip (return null) unless the source is enabled and due, checked once the source's lock is held.</param>
    /// <exception cref="KeyNotFoundException">No such source.</exception>
    public async Task<SourceRefreshResult?> RefreshAsync(int sourceId, bool onlyIfDue = false, CancellationToken cancellationToken = default)
    {
        var sourceLock = _perSource.GetOrAdd(sourceId, _ => new SemaphoreSlim(1));
        await sourceLock.WaitAsync(cancellationToken);
        try
        {
            await _global.WaitAsync(cancellationToken);
            try
            {
                return await RefreshLockedAsync(sourceId, onlyIfDue, cancellationToken);
            }
            finally
            {
                _global.Release();
            }
        }
        finally
        {
            sourceLock.Release();
        }
    }

    private async Task<SourceRefreshResult?> RefreshLockedAsync(int sourceId, bool onlyIfDue, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var source = await db.Sources.SingleOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
                     ?? throw new KeyNotFoundException($"Source {sourceId} not found");
        if (onlyIfDue && !IsDue(source, time.GetUtcNow().UtcDateTime))
        {
            return null;
        }

        SourceFetchResult fetched;
        try
        {
            var type = types.Find(source.Type) ?? throw new SourceFetchException($"Unknown source type '{source.Type}'");
            fetched = await type.FetchAsync(source.Settings, new SourceFetchOptions { BypassCache = true }, cancellationToken);
        }
        catch (SourceFetchException e)
        {
            return await FailAsync(db, source, e.Message, cancellationToken);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var events = await db.Events.Where(e => e.SourceId == sourceId).ToDictionaryAsync(e => e.Key, StringComparer.Ordinal, cancellationToken);

        if (fetched.Events.Count == 0 && events.Values.Any(e => e.MissedRefreshes == 0))
        {
            var errors = fetched.ItemErrors.Count > 0 ? $" ({fetched.ItemErrors.Count} item errors)" : "";
            return await FailAsync(db, source, $"No events returned, but the previous refresh returned some{errors}", cancellationToken);
        }

        int added = 0, changed = 0, unchanged = 0, gone = 0;
        foreach (var extracted in fetched.Events)
        {
            var hash = EventHash.Compute(extracted);
            if (!events.Remove(extracted.Key, out var existing))
            {
                db.Events.Add(NewEvent(source.Id, extracted, hash, now));
                added++;
                continue;
            }

            existing.LastSeen = now;
            existing.MissedRefreshes = 0;
            if (existing.Hash == hash && existing.Status != EventStatus.Gone)
            {
                unchanged++;
                continue;
            }

            db.EventRevisions.Add(Snapshot(existing, now));
            Apply(existing, extracted, hash, now);
            changed++;
        }

        // Items that failed extraction but carry a key are not missing: the source still returns them.
        var erroredKeys = fetched.ItemErrors
            .Select(e => FieldValues.ToText(FieldValues.FromJson(e.Fields[ReservedFields.Key])))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        foreach (var missing in events.Values.Where(e => !erroredKeys.Contains(e.Key)))
        {
            missing.MissedRefreshes++;
            if (missing.MissedRefreshes >= MissedRefreshesUntilGone && missing.Status != EventStatus.Gone)
            {
                missing.Status = EventStatus.Gone;
                missing.LastChanged = now;
                gone++;
            }
        }

        source.LastRefresh = now;
        source.LastSuccess = now;
        source.LastError = null;
        source.Meta = fetched.Meta;
        await db.SaveChangesAsync(cancellationToken);

        var cutoff = now - RevisionRetention;
        await db.EventRevisions
            .Where(r => r.Created < cutoff && db.Events.Any(e => e.Id == r.EventId && e.SourceId == sourceId))
            .ExecuteDeleteAsync(cancellationToken);

        var result = new SourceRefreshResult(source.Id, source.Name, true, added, changed, unchanged, gone, null);
        logger.LogInformation("Refreshed source {Summary}", result.Summary);
        return result;
    }

    private async Task<SourceRefreshResult> FailAsync(GenerralDbContext db, Source source, string error, CancellationToken cancellationToken)
    {
        // Shown in the UI and in command messages: URLs may carry API keys (e.g. TheSportsDB's path).
        error = CleanseLogMessage.Cleanse(error);
        source.LastRefresh = time.GetUtcNow().UtcDateTime;
        source.LastError = error;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Refreshing source {Name} failed: {Error}", source.Name, error);
        return new SourceRefreshResult(source.Id, source.Name, false, 0, 0, 0, 0, error);
    }

    private static Event NewEvent(int sourceId, ExtractedEvent extracted, string hash, DateTime now)
    {
        var ev = new Event { SourceId = sourceId, Key = extracted.Key, Title = extracted.Title, Hash = hash, FirstSeen = now, LastSeen = now };
        Apply(ev, extracted, hash, now);
        return ev;
    }

    private static void Apply(Event ev, ExtractedEvent extracted, string hash, DateTime now)
    {
        ev.Title = extracted.Title;
        ev.Start = extracted.Start;
        ev.End = extracted.End;
        ev.Timezone = extracted.Timezone;
        ev.Status = extracted.Status;
        ev.Fields = extracted.Fields;
        ev.Images = extracted.Images;
        ev.Hash = hash;
        ev.LastChanged = now;
    }

    private static EventRevision Snapshot(Event ev, DateTime now) => new()
    {
        EventId = ev.Id,
        Created = now,
        Title = ev.Title,
        Start = ev.Start,
        End = ev.End,
        Status = ev.Status,
        Fields = (JsonObject)ev.Fields.DeepClone(),
        Images = ev.Images,
        Hash = ev.Hash,
    };
}
