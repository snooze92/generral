using System.Collections.Concurrent;
using Generral.Core.Datastore;
using Generral.Core.Extras.Metadata;
using Generral.Core.HealthCheck;
using Generral.Core.Notifications;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Mappings;

public sealed record ApplyMappingResult(int MappingId, string MappingName, int Added, int Changed, int Unchanged, int Orphaned, int Errors, string? Error)
{
    public bool Success => Error is null;

    public string Summary => Success
        ? $"{MappingName}: {Added} added, {Changed} changed, {Unchanged} unchanged, {Orphaned} orphaned, {Errors} errors"
        : $"{MappingName}: {Error}";
}

/// <summary>An episode whose number a renumber changes.</summary>
public sealed record RenumberMove(
    int EpisodeId, int MappingId, string EventKey, string? PartKey, string Title, int SeasonNumber, int EpisodeNumber, int NewSeasonNumber, int NewEpisodeNumber);

public sealed record RenumberResult(IReadOnlyList<RenumberMove> Moves, IReadOnlyList<ApplyMappingResult> Mappings);

/// <summary>Evaluates mappings and persists the plan: seasons, episodes, links and orphan status (docs/specs/05-mapping.md).</summary>
public sealed class ApplyMappingsService(
    IServiceScopeFactory scopes, MappingEngine engine, ProviderStatusTracker status, TimeProvider time, ILogger<ApplyMappingsService> logger)
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _perSeries = new();

    /// <summary>Series timezone (container <c>TZ</c>); per-series timezones are not configurable yet.</summary>
    public TimeZoneInfo Timezone { get; init; } = TimeZoneInfo.Local;

    /// <exception cref="KeyNotFoundException">No such mapping.</exception>
    public async Task<ApplyMappingResult> ApplyAsync(int mappingId, CancellationToken cancellationToken = default)
    {
        int seriesId;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
            seriesId = await db.Mappings.Where(m => m.Id == mappingId).Select(m => (int?)m.SeriesId).SingleOrDefaultAsync(cancellationToken)
                       ?? throw new KeyNotFoundException($"Mapping {mappingId} not found");
        }

        return await WithSeriesLockAsync(seriesId, async () =>
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
            var mapping = await db.Mappings.Include(m => m.Series).Include(m => m.Source).SingleOrDefaultAsync(m => m.Id == mappingId, cancellationToken)
                          ?? throw new KeyNotFoundException($"Mapping {mappingId} not found");
            var result = (await ApplyLockedAsync(db, mapping, false, cancellationToken)).Result;
            await SyncMetadataAsync(scope.ServiceProvider, seriesId, cancellationToken);
            return result;
        });
    }

    /// <summary>
    /// Series → Renumber (docs/specs/05-mapping.md § Renumber): applies every enabled mapping of the series, in priority order,
    /// with unlocked episodes taking their ideal numbers. With <paramref name="dryRun"/> nothing is stored.
    /// </summary>
    /// <returns>The episodes whose number changes.</returns>
    /// <exception cref="KeyNotFoundException">No such series.</exception>
    public Task<RenumberResult> RenumberAsync(int seriesId, bool dryRun, CancellationToken cancellationToken = default) =>
        WithSeriesLockAsync(seriesId, async () =>
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
            if (!await db.Series.AnyAsync(s => s.Id == seriesId, cancellationToken))
            {
                throw new KeyNotFoundException($"Series {seriesId} not found");
            }

            var before = await db.Episodes.AsNoTracking().Where(e => e.SeriesId == seriesId)
                .ToDictionaryAsync(e => e.Id, e => (e.SeasonNumber, e.EpisodeNumber), cancellationToken);
            var mappings = await db.Mappings.Include(m => m.Series).Include(m => m.Source)
                .Where(m => m.SeriesId == seriesId && m.Enabled)
                .OrderBy(m => m.Priority).ThenBy(m => m.Id)
                .ToListAsync(cancellationToken);

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var results = new List<ApplyMappingResult>();
            var moves = new List<RenumberMove>();
            foreach (var mapping in mappings)
            {
                var (result, plan) = await ApplyLockedAsync(db, mapping, true, cancellationToken);
                results.Add(result);
                moves.AddRange((plan?.Rows ?? [])
                    .Where(r => r.IsApplied && r.EpisodeId is { } id && before.TryGetValue(id, out var old) && old != (r.SeasonNumber!.Value, r.EpisodeNumber!.Value))
                    .Select(r => new RenumberMove(r.EpisodeId!.Value, mapping.Id, r.Event.Key, r.PartKey, r.Title!,
                        before[r.EpisodeId!.Value].SeasonNumber, before[r.EpisodeId!.Value].EpisodeNumber, r.SeasonNumber!.Value, r.EpisodeNumber!.Value)));
            }

            if (dryRun)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            else
            {
                await transaction.CommitAsync(cancellationToken);
                await SyncMetadataAsync(scope.ServiceProvider, seriesId, cancellationToken);
            }

            return new RenumberResult(moves.OrderBy(m => m.NewSeasonNumber).ThenBy(m => m.NewEpisodeNumber).ToList(), results);
        });

    /// <summary>Rewrites the NFO files whose episodes changed (spec 08 § NFO files) and has media servers rescan the series.</summary>
    private static async Task SyncMetadataAsync(IServiceProvider services, int seriesId, CancellationToken cancellationToken)
    {
        if (await services.GetRequiredService<MetadataService>().SyncSeriesAsync(seriesId, cancellationToken) > 0 &&
            await services.GetRequiredService<GenerralDbContext>().Series.AsNoTracking().SingleOrDefaultAsync(s => s.Id == seriesId, cancellationToken) is { } series)
        {
            await services.GetRequiredService<NotificationService>().UpdateMediaServersAsync(series, cancellationToken);
        }
    }

    private async Task<T> WithSeriesLockAsync<T>(int seriesId, Func<Task<T>> action)
    {
        // Mappings of one series share its episode numbers.
        var seriesLock = _perSeries.GetOrAdd(seriesId, _ => new SemaphoreSlim(1));
        await seriesLock.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            seriesLock.Release();
        }
    }

    private async Task<(ApplyMappingResult Result, MappingPlan? Plan)> ApplyLockedAsync(GenerralDbContext db, Mapping mapping, bool renumber, CancellationToken cancellationToken)
    {
        var series = mapping.Series!;
        await MappingHistory.RecordAsync(db, mapping, cancellationToken);

        MappingPlan plan;
        try
        {
            var definition = MappingDefinitionParser.FromYaml(mapping.Definition);
            var episodeList = await db.Episodes.Where(e => e.SeriesId == series.Id).ToListAsync(cancellationToken);
            plan = engine.Evaluate(
                await InputAsync(db, mapping, definition, episodeList, renumber, cancellationToken) with
                {
                    Claims = definition.Dedupe is null ? [] : await ClaimsAsync(db, mapping, episodeList, cancellationToken),
                },
                cancellationToken);
        }
        catch (MappingDefinitionException e)
        {
            await db.SaveChangesAsync(cancellationToken);
            status.RecordFailure(ProviderKind.Mapping, mapping.Id, mapping.Name, e.Message);
            logger.LogWarning("Mapping {Name} is invalid: {Error}", mapping.Name, e.Message);
            return (new ApplyMappingResult(mapping.Id, mapping.Name, 0, 0, 0, 0, 0, e.Message), null);
        }

        // Episodes are tracked (loaded above); applying in two passes avoids transient unique-index clashes when numbers swap.
        var episodes = db.Episodes.Local.ToDictionary(e => e.Id);
        var seasons = await db.Seasons.Where(s => s.SeriesId == series.Id).ToDictionaryAsync(s => s.SeasonNumber, cancellationToken);
        foreach (var (number, title) in plan.Seasons)
        {
            var poster = plan.SeasonPosters.GetValueOrDefault(number);
            if (!seasons.TryGetValue(number, out var season))
            {
                db.Seasons.Add(new Season
                {
                    SeriesId = series.Id,
                    SeasonNumber = number,
                    Title = title,
                    Monitored = series.MonitorNewItems == NewItemMonitorType.All,
                    Images = new ImageSet { Poster = poster },
                });
                continue;
            }

            if (title is not null)
            {
                season.Title = title;
            }

            if (poster is not null)
            {
                season.Images = season.Images with { Poster = poster };
            }
        }

        series.SeriesType = plan.SeriesType;

        var moved = plan.Rows
            .Where(r => r.IsApplied && r.EpisodeId is { } id && (episodes[id].SeasonNumber != r.SeasonNumber || episodes[id].EpisodeNumber != r.EpisodeNumber))
            .Select(r => episodes[r.EpisodeId!.Value])
            .ToList();
        if (moved.Count > 0)
        {
            await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
            foreach (var episode in moved)
            {
                episode.EpisodeNumber = -episode.Id;
            }

            await db.SaveChangesAsync(cancellationToken);
            await SaveAsync(db, plan, mapping, episodes, cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        else
        {
            await SaveAsync(db, plan, mapping, episodes, cancellationToken);
        }

        var counts = plan.Rows.GroupBy(r => r.Change).ToDictionary(g => g.Key, g => g.Count());
        var result = new ApplyMappingResult(
            mapping.Id,
            mapping.Name,
            counts.GetValueOrDefault(MappingChange.New),
            counts.GetValueOrDefault(MappingChange.Changed),
            counts.GetValueOrDefault(MappingChange.Unchanged),
            counts.GetValueOrDefault(MappingChange.Orphaned),
            counts.GetValueOrDefault(MappingChange.Error),
            null);
        if (result.Errors > 0)
        {
            status.RecordFailure(ProviderKind.Mapping, mapping.Id, mapping.Name, $"{result.Errors} event(s) could not be mapped");
        }
        else
        {
            status.RecordSuccess(ProviderKind.Mapping, mapping.Id);
        }

        logger.LogInformation("Applied mapping {Summary}", result.Summary);
        return (result, plan);
    }

    private async Task<MappingInput> InputAsync(
        GenerralDbContext db, Mapping mapping, MappingDefinition definition, List<Episode> episodes, bool renumber, CancellationToken cancellationToken) => new()
        {
            Definition = definition,
            Series = mapping.Series!,
            Source = mapping.Source!,
            SourceMeta = mapping.Source!.Meta,
            Events = await db.Events.AsNoTracking().Where(e => e.SourceId == mapping.SourceId).ToListAsync(cancellationToken),
            Episodes = episodes,
            Links = await db.EpisodeLinks.AsNoTracking().Where(l => l.MappingId == mapping.Id).ToListAsync(cancellationToken),
            Renumber = renumber,
            Now = time.GetUtcNow().UtcDateTime,
            Timezone = Timezone,
        };

    /// <summary>The <c>dedupe</c> keys of the series' other enabled mappings (rule 6), each evaluated on its own.</summary>
    public async Task<List<DedupeClaim>> ClaimsAsync(GenerralDbContext db, Mapping mapping, List<Episode> episodes, CancellationToken cancellationToken)
    {
        var claims = new List<DedupeClaim>();
        var others = await db.Mappings.Include(m => m.Series).Include(m => m.Source)
            .Where(m => m.SeriesId == mapping.SeriesId && m.Enabled && m.Id != mapping.Id)
            .ToListAsync(cancellationToken);
        foreach (var other in others)
        {
            try
            {
                var definition = MappingDefinitionParser.FromYaml(other.Definition);
                if (definition.Dedupe is not null)
                {
                    var plan = engine.Evaluate(await InputAsync(db, other, definition, episodes, false, cancellationToken), cancellationToken);
                    claims.AddRange(MappingEngine.ClaimsOf(plan, other, mapping));
                }
            }
            catch (MappingDefinitionException)
            {
                // Reported when that mapping is applied.
            }
        }

        return claims;
    }

    private static async Task SaveAsync(GenerralDbContext db, MappingPlan plan, Mapping mapping, Dictionary<int, Episode> episodes, CancellationToken cancellationToken)
    {
        foreach (var row in plan.Rows)
        {
            switch (row.Change)
            {
                case MappingChange.New:
                    var episode = new Episode { SeriesId = mapping.SeriesId, Title = "", Monitored = row.Monitored ?? true };
                    row.ApplyTo(episode);
                    db.EpisodeLinks.Add(new EpisodeLink { MappingId = mapping.Id, EventId = row.Event.Id, PartKey = row.LinkPartKey, Episode = episode });
                    break;
                case MappingChange.Changed:
                    row.ApplyTo(episodes[row.EpisodeId!.Value]);
                    if (!row.IsLinked)
                    {
                        // Taken over from a lower-priority mapping (dedupe).
                        await db.EpisodeLinks.Where(l => l.EpisodeId == row.EpisodeId && l.PartKey == row.LinkPartKey).ExecuteDeleteAsync(cancellationToken);
                        db.EpisodeLinks.Add(new EpisodeLink { MappingId = mapping.Id, EventId = row.Event.Id, PartKey = row.LinkPartKey, EpisodeId = row.EpisodeId!.Value });
                    }

                    break;
                case MappingChange.Orphaned:
                    episodes[row.EpisodeId!.Value].Status = EpisodeStatus.Orphaned;
                    break;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
