using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.DecisionEngine;
using Generral.Core.Download.TrackedDownloads;
using Generral.Core.Messaging;
using Generral.Core.Tv;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Core.IndexerSearch;

/// <summary>Spec 06 § Search triggers: automatic searches at fixed offsets after an episode airs.</summary>
public static class OnAirSchedule
{
    /// <summary><c>airDateUtc</c> + 1h, 3h, 6h, 12h, 24h, 48h, then daily for 7 days (day 3 to day 9).</summary>
    public static readonly IReadOnlyList<TimeSpan> Default =
    [
        .. new[] { 1, 3, 6, 12, 24, 48 }.Select(h => TimeSpan.FromHours(h)),
        .. Enumerable.Range(3, 7).Select(d => TimeSpan.FromDays(d)),
    ];

    /// <summary>Offsets beyond this are ignored, which bounds the episodes the scheduler looks at.</summary>
    public static readonly TimeSpan MaxOffset = TimeSpan.FromDays(60);

    /// <summary>A slot that passed more than this long ago is skipped (e.g. after downtime) rather than searched late.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    /// <summary>The latest passed slot if the episode hasn't been searched since, else null.</summary>
    public static DateTime? DueSlot(DateTime airDateUtc, DateTime? lastSearchTime, IReadOnlyList<TimeSpan> schedule, DateTime now)
    {
        var slot = schedule.Where(offset => offset <= MaxOffset).Select(offset => airDateUtc + offset).Where(s => s <= now).DefaultIfEmpty(DateTime.MinValue).Max();
        return slot != DateTime.MinValue && now - slot <= Grace && (lastSearchTime is null || lastSearchTime < slot) ? slot : null;
    }

    /// <summary>Monitored episodes of monitored series without a file or queued download, with a due slot.</summary>
    public static async Task<List<int>> DueEpisodesAsync(GenerralDbContext db, IReadOnlyDictionary<int, string> queued, DateTime now, CancellationToken cancellationToken = default)
    {
        var earliest = now - MaxOffset - Grace;
        var episodes = db.Episodes.Where(e => e.Status == EpisodeStatus.Active && e.Monitored && e.Series!.Monitored && e.EpisodeFileId == null &&
            e.AirDateUtc <= now && e.AirDateUtc >= earliest);
        var candidates = await ReleaseSearchService.LoadCandidatesAsync(db, episodes, cancellationToken);
        return candidates
            .Where(c => !queued.ContainsKey(c.Episode.Id))
            .Where(c => DueSlot(c.Episode.AirDateUtc!.Value, c.Episode.LastSearchTime, c.Terms.Schedule, now) is not null)
            .Select(c => c.Episode.Id)
            .ToList();
    }
}

/// <summary><c>OnAirSearch</c>: queued by the scheduler when episodes are due; searches them automatically (monitored only).</summary>
public sealed class OnAirSearchHandler(ReleaseSearchService search, TrackedDownloadService tracked, IServiceScopeFactory scopes, TimeProvider time) : ICommandHandler
{
    public const string CommandName = "OnAirSearch";

    public string Name => CommandName;

    public async Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken)
    {
        List<int> due;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
            due = await OnAirSchedule.DueEpisodesAsync(db, tracked.QueuedEpisodes(), time.GetUtcNow().UtcDateTime, cancellationToken);
        }

        if (due.Count == 0)
        {
            return "No episode due";
        }

        var result = await search.AutomaticSearchAsync(e => due.Contains(e.Id), userInvoked: false, cancellationToken);
        return result.Summary;
    }
}
