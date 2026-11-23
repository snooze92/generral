// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/IndexerSearch/ReleaseSearchService.cs, src/NzbDrone.Core/Indexers/RssSyncService.cs
// and src/NzbDrone.Core/Download/ProcessDownloadDecisions.cs
using System.Linq.Expressions;
using Generral.Core.Datastore;
using Generral.Core.DecisionEngine;
using Generral.Core.Download;
using Generral.Core.Download.TrackedDownloads;
using Generral.Core.HealthCheck;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.Instrumentation;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Generral.Core.IndexerSearch;

/// <summary>Interactive search results, best first, and the indexers that timed out (their late results are missing).</summary>
public sealed record InteractiveSearchResult(List<DownloadDecision> Decisions, IReadOnlyList<string> TimedOut);

public sealed record SearchResult(List<DownloadDecision> Decisions, int Searched, int Grabbed)
{
    public string Summary => $"{Searched} episode(s) searched, {Decisions.Count} release(s) found, {Grabbed} grabbed";
}

/// <summary>Queries the indexers with the episodes' rendered search terms, evaluates the results and grabs the best accepted ones.</summary>
public sealed class ReleaseSearchService(
    IServiceScopeFactory scopes,
    TorznabClient torznab,
    DownloadDecisionMaker decisionMaker,
    IDownloadService downloads,
    TrackedDownloadService trackedDownloads,
    IndexerStatusService status,
    TimeProvider time,
    ILogger<ReleaseSearchService> logger)
{
    public const int MaxParallelIndexers = 8;

    /// <summary>Budget of one indexer within one automatic search or RSS sync (all its queries).</summary>
    public TimeSpan IndexerTimeout { get; internal set; } = TimeSpan.FromSeconds(30);

    /// <summary>Shorter budget for interactive search, where someone is waiting for the results.</summary>
    public TimeSpan InteractiveIndexerTimeout { get; internal set; } = TimeSpan.FromSeconds(15);

    /// <summary>Back-off after a request limit that didn't say how long to wait (Sonarr's <c>minimumBackoff</c>).</summary>
    public static readonly TimeSpan RequestLimitBackOff = TimeSpan.FromHours(1);

    /// <summary>In memory: after a restart RSS sync runs at the first scheduler check.</summary>
    public DateTime? LastRssSync { get; private set; }

    /// <summary>Interactive search: every decision, nothing grabbed, in <see cref="DownloadDecisionComparer.InteractiveOrder"/>.</summary>
    public async Task<InteractiveSearchResult> InteractiveSearchAsync(int episodeId, CancellationToken cancellationToken = default)
    {
        var (decisions, _, timedOut) = await SearchAsync(e => e.Id == episodeId, interactive: true, userInvoked: true, cancellationToken);
        return new InteractiveSearchResult(DownloadDecisionComparer.InteractiveOrder(decisions), timedOut);
    }

    /// <summary>Automatic search (EpisodeSearch, SeasonSearch, SeriesSearch, MissingEpisodeSearch): grabs the best accepted releases.</summary>
    public async Task<SearchResult> AutomaticSearchAsync(Expression<Func<Episode, bool>> episodes, bool userInvoked, CancellationToken cancellationToken = default)
    {
        var (decisions, searched, _) = await SearchAsync(episodes, interactive: false, userInvoked, cancellationToken);
        var grabbed = await GrabAsync(decisions, userInvoked ? ReleaseSourceType.UserInvokedSearch : ReleaseSourceType.Search, cancellationToken);
        return new SearchResult(decisions, searched, grabbed);
    }

    /// <summary>
    /// RSS sync: each RSS-enabled indexer's latest releases against every active episode without a file (or below its cutoff) whose date window is open
    /// (spec 06 § Search triggers); unmonitored ones only serve to detect ambiguity.
    /// </summary>
    public async Task<SearchResult> RssSyncAsync(CancellationToken cancellationToken = default)
    {
        LastRssSync = time.GetUtcNow().UtcDateTime;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var indexers = await SearchableIndexersAsync(db, i => i.EnableRss, cancellationToken);
        var (releases, _) = await QueryIndexersAsync(indexers, IndexerTimeout, async (indexer, found, token) => found.AddRange(await FetchAsync(indexer, new TorznabSearch(), token)), cancellationToken);

        var now = time.GetUtcNow().UtcDateTime;
        var upgradable = await EpisodeCutoffService.CutoffUnmetEpisodeIdsAsync(db, cancellationToken);
        var candidates = (await LoadCandidatesAsync(db, db.Episodes.Where(e => e.Status == EpisodeStatus.Active && (e.EpisodeFileId == null || upgradable.Contains(e.Id)) && e.AirDateUtc != null), cancellationToken))
            .Where(c => c.Episode.AirDateUtc - c.Terms.Before <= now && now <= c.Episode.AirDateUtc + c.Terms.After)
            .ToList();
        var context = await ContextAsync(db, candidates, null, indexers, userInvoked: false, cancellationToken);
        var decisions = decisionMaker.GetDecisions(Distinct(releases), context);
        var grabbed = await GrabAsync(decisions, ReleaseSourceType.Rss, cancellationToken);
        logger.LogInformation("RSS sync: {Releases} release(s) from {Indexers} indexer(s), {Grabbed} grabbed", decisions.Count, indexers.Count, grabbed);
        return new SearchResult(decisions, candidates.Count, grabbed);
    }

    private async Task<(List<DownloadDecision> Decisions, int Searched, IReadOnlyList<string> TimedOut)> SearchAsync(
        Expression<Func<Episode, bool>> filter,
        bool interactive,
        bool userInvoked,
        CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var requested = await db.Episodes.Where(e => e.Status == EpisodeStatus.Active).Where(filter).Select(e => e.Id).ToListAsync(cancellationToken);
        if (requested.Count == 0)
        {
            return ([], 0, []);
        }

        var seriesIds = await db.Episodes.Where(e => requested.Contains(e.Id)).Select(e => e.SeriesId).Distinct().ToListAsync(cancellationToken);
        var scopeCandidates = await LoadCandidatesAsync(db, db.Episodes.Where(e => seriesIds.Contains(e.SeriesId) && e.Status == EpisodeStatus.Active), cancellationToken);
        var indexers = await SearchableIndexersAsync(db, interactive ? i => i.EnableInteractiveSearch : i => i.EnableAutomaticSearch, cancellationToken);

        var searched = scopeCandidates.Where(c => requested.Contains(c.Episode.Id)).ToList();
        foreach (var candidate in searched.Where(c => c.Terms.Queries.Count == 0))
        {
            logger.LogInformation("{Series} {Episode} has no search queries", candidate.Episode.Series?.Title, candidate.Label);
        }

        var (releases, timedOut) = await QueryIndexersAsync(indexers, interactive ? InteractiveIndexerTimeout : IndexerTimeout, async (indexer, found, token) =>
        {
            var done = new HashSet<(string, string)>();
            foreach (var candidate in searched)
            {
                foreach (var query in candidate.Terms.Queries)
                {
                    if (status.IsDisabled(indexer.Id))
                    {
                        return;
                    }

                    var categories = candidate.Terms.Categories;
                    if (!done.Add((query, string.Join(',', categories ?? []))))
                    {
                        continue;
                    }

                    var results = await FetchAsync(indexer, new TorznabSearch { Query = query, Categories = categories?.Count > 0 ? categories : null }, token);
                    found.AddRange(results);
                    if (results.Count > 0 && candidate.Terms.QueryMode == QueryMode.FirstWithResults)
                    {
                        break;
                    }
                }
            }
        }, cancellationToken);

        await db.Episodes.Where(e => requested.Contains(e.Id))
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.LastSearchTime, time.GetUtcNow().UtcDateTime), cancellationToken);

        var context = await ContextAsync(db, scopeCandidates, requested.ToHashSet(), indexers, userInvoked, cancellationToken);
        return (decisionMaker.GetDecisions(Distinct(releases), context), requested.Count, timedOut);
    }

    /// <summary>Like Sonarr's <c>ProcessDecisions</c>: best first, never two releases for the same episode.</summary>
    private async Task<int> GrabAsync(List<DownloadDecision> decisions, ReleaseSourceType source, CancellationToken cancellationToken)
    {
        var grabbedEpisodes = new HashSet<int>();
        var grabbed = 0;
        foreach (var decision in DownloadDecisionComparer.Prioritize(decisions.Where(d => d.Approved)))
        {
            var episodes = decision.RemoteEpisode.Episodes;
            if (episodes.Any(e => grabbedEpisodes.Contains(e.Id)))
            {
                continue;
            }

            try
            {
                decision.RemoteEpisode.ReleaseSource = source;
                await downloads.GrabAsync(decision.RemoteEpisode, cancellationToken);
                grabbedEpisodes.UnionWith(episodes.Select(e => e.Id));
                grabbed++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Couldn't grab {Title}", decision.RemoteEpisode.Release.Title);
            }
        }

        return grabbed;
    }

    public async Task<DecisionContext> ContextAsync(
        GenerralDbContext db,
        IReadOnlyList<SearchCandidate> candidates,
        IReadOnlySet<int>? requested,
        IReadOnlyList<Indexer>? indexers,
        bool userInvoked,
        CancellationToken cancellationToken = default)
    {
        var seriesIds = candidates.Select(c => c.Episode.SeriesId).Distinct().ToList();
        var fileIds = candidates.Select(c => c.Episode.EpisodeFileId).OfType<int>().Distinct().ToList();
        return new DecisionContext
        {
            EpisodeFiles = await db.EpisodeFiles.AsNoTracking().Where(f => fileIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken),
            Scope = candidates,
            Requested = requested,
            QualityProfiles = await db.QualityProfiles.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken),
            ReleaseProfiles = await db.ReleaseProfiles.AsNoTracking().ToListAsync(cancellationToken),
            Indexers = (indexers ?? await db.Indexers.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(i => i.Id),
            UserInvoked = userInvoked,
            Blocklist = (await db.Blocklist.AsNoTracking().Where(b => seriesIds.Contains(b.SeriesId)).ToListAsync(cancellationToken)).ToLookup(b => b.SeriesId),
            QueuedEpisodes = trackedDownloads.QueuedEpisodes(),
        };
    }

    public static async Task<List<SearchCandidate>> LoadCandidatesAsync(GenerralDbContext db, IQueryable<Episode> episodes, CancellationToken cancellationToken = default) =>
        (await episodes.AsNoTracking().Include(e => e.Series).OrderBy(e => e.SeriesId).ThenBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToListAsync(cancellationToken))
        .Select(e => new SearchCandidate(e, SearchTerms.From(e.SearchTerms, e.Series?.SearchSettings)))
        .ToList();

    /// <summary>Enabled Torznab indexers, without those in back-off (Sonarr's <c>filterBlockedIndexers</c>).</summary>
    private async Task<List<Indexer>> SearchableIndexersAsync(GenerralDbContext db, Func<Indexer, bool> enabled, CancellationToken cancellationToken)
    {
        var indexers = (await db.Indexers.AsNoTracking().OrderBy(i => i.Priority).ThenBy(i => i.Id).ToListAsync(cancellationToken))
            .Where(i => enabled(i) && i.Implementation == IndexerImplementations.Torznab)
            .ToList();
        foreach (var blocked in indexers.Where(i => status.IsDisabled(i.Id)))
        {
            logger.LogDebug("Indexer {Indexer} is temporarily disabled due to recent failures, skipped", blocked.Name);
        }

        return indexers.Where(i => !status.IsDisabled(i.Id)).ToList();
    }

    /// <summary>
    /// Runs <paramref name="query"/> for up to <see cref="MaxParallelIndexers"/> indexers at once, each within <paramref name="budget"/>:
    /// a slow or failing indexer only loses its own (remaining) results. A timeout is not a failure (no back-off).
    /// </summary>
    private async Task<(List<ReleaseInfo> Releases, List<string> TimedOut)> QueryIndexersAsync(
        IReadOnlyList<Indexer> indexers,
        TimeSpan budget,
        Func<Indexer, List<ReleaseInfo>, CancellationToken, Task> query,
        CancellationToken cancellationToken)
    {
        var perIndexer = new List<ReleaseInfo>[indexers.Count];
        var timedOut = new bool[indexers.Count];
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelIndexers, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(Enumerable.Range(0, indexers.Count), options, async (i, token) =>
        {
            var found = perIndexer[i] = [];
            using var timeout = new CancellationTokenSource(budget, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
            try
            {
                await query(indexers[i], found, linked.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !token.IsCancellationRequested)
            {
                timedOut[i] = true;
                logger.LogWarning("Indexer {Indexer} timed out after {Seconds}s, {Count} release(s) kept", indexers[i].Name, budget.TotalSeconds, found.Count);
            }
        });

        return (perIndexer.SelectMany(r => r).ToList(), indexers.Where((_, i) => timedOut[i]).Select(i => i.Name).ToList());
    }

    private async Task<IReadOnlyList<ReleaseInfo>> FetchAsync(Indexer indexer, TorznabSearch search, CancellationToken cancellationToken)
    {
        try
        {
            var releases = await torznab.SearchAsync(indexer, search, cancellationToken);
            status.RecordSuccess(indexer);
            return releases;
        }
        catch (Exception e) when (e is TorznabException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var failure = status.RecordFailure(indexer, CleanseLogMessage.Cleanse(e.Message), MinimumBackOff(e), escalate: !IsConnectionFailure(e));
            logger.LogWarning("Indexer {Indexer} failed, disabled till {DisabledTill:u}: {Message}", indexer.Name, failure.DisabledTill, e.Message);
            return [];
        }
    }

    private TimeSpan MinimumBackOff(Exception e)
    {
        if (e is not TorznabException { RequestLimit: true } limit)
        {
            return TimeSpan.Zero;
        }

        // A disabled-till time already past (e.g. Prowlarr in another time zone) falls back to the normal escalation.
        var untilDisabledTill = limit.DisabledTill - time.GetUtcNow().UtcDateTime;
        return limit.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero ? retryAfter
            : untilDisabledTill is { } until ? (until > TimeSpan.Zero ? until : TimeSpan.Zero)
            : RequestLimitBackOff;
    }

    private static bool IsConnectionFailure(Exception e) =>
        e is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError };

    private static IEnumerable<ReleaseInfo> Distinct(IEnumerable<ReleaseInfo> releases) =>
        releases.DistinctBy(r => r.InfoHash is { Length: > 0 } hash ? hash.ToUpperInvariant() : $"{r.IndexerId}:{r.Guid}");
}
