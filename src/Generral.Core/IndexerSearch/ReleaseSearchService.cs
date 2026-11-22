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
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Generral.Core.IndexerSearch;

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
    TimeProvider time,
    ILogger<ReleaseSearchService> logger)
{
    /// <summary>In memory: after a restart RSS sync runs at the first scheduler check.</summary>
    public DateTime? LastRssSync { get; private set; }

    /// <summary>Interactive search: every decision, nothing grabbed. Accepted first, then rejected matches, then unmatched releases, each best first.</summary>
    public async Task<List<DownloadDecision>> InteractiveSearchAsync(int episodeId, CancellationToken cancellationToken = default)
    {
        var (decisions, _) = await SearchAsync(e => e.Id == episodeId, interactive: true, userInvoked: true, cancellationToken);
        return decisions
            .OrderBy(d => d.Approved ? 0 : d.RemoteEpisode.Episodes.Count > 0 ? 1 : 2)
            .ThenByDescending(d => d, DownloadDecisionComparer.Instance)
            .ToList();
    }

    /// <summary>Automatic search (EpisodeSearch, SeasonSearch, SeriesSearch, MissingEpisodeSearch): grabs the best accepted releases.</summary>
    public async Task<SearchResult> AutomaticSearchAsync(Expression<Func<Episode, bool>> episodes, bool userInvoked, CancellationToken cancellationToken = default)
    {
        var (decisions, searched) = await SearchAsync(episodes, interactive: false, userInvoked, cancellationToken);
        var grabbed = await GrabAsync(decisions, cancellationToken);
        return new SearchResult(decisions, searched, grabbed);
    }

    /// <summary>
    /// RSS sync: each RSS-enabled indexer's latest releases against every active episode without a file whose date window is open
    /// (spec 06 § Search triggers); unmonitored ones only serve to detect ambiguity.
    /// </summary>
    public async Task<SearchResult> RssSyncAsync(CancellationToken cancellationToken = default)
    {
        LastRssSync = time.GetUtcNow().UtcDateTime;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var indexers = await SearchableIndexersAsync(db, i => i.EnableRss, cancellationToken);
        var releases = new List<ReleaseInfo>();
        foreach (var indexer in indexers)
        {
            releases.AddRange(await FetchAsync(indexer, new TorznabSearch(), cancellationToken));
        }

        var now = time.GetUtcNow().UtcDateTime;
        var candidates = (await LoadCandidatesAsync(db, db.Episodes.Where(e => e.Status == EpisodeStatus.Active && e.EpisodeFileId == null && e.AirDateUtc != null), cancellationToken))
            .Where(c => c.Episode.AirDateUtc - c.Terms.Before <= now && now <= c.Episode.AirDateUtc + c.Terms.After)
            .ToList();
        var context = await ContextAsync(db, candidates, null, indexers, userInvoked: false, cancellationToken);
        var decisions = decisionMaker.GetDecisions(Distinct(releases), context);
        var grabbed = await GrabAsync(decisions, cancellationToken);
        logger.LogInformation("RSS sync: {Releases} release(s) from {Indexers} indexer(s), {Grabbed} grabbed", decisions.Count, indexers.Count, grabbed);
        return new SearchResult(decisions, candidates.Count, grabbed);
    }

    private async Task<(List<DownloadDecision> Decisions, int Searched)> SearchAsync(
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
            return ([], 0);
        }

        var seriesIds = await db.Episodes.Where(e => requested.Contains(e.Id)).Select(e => e.SeriesId).Distinct().ToListAsync(cancellationToken);
        var scopeCandidates = await LoadCandidatesAsync(db, db.Episodes.Where(e => seriesIds.Contains(e.SeriesId) && e.Status == EpisodeStatus.Active), cancellationToken);
        var indexers = await SearchableIndexersAsync(db, interactive ? i => i.EnableInteractiveSearch : i => i.EnableAutomaticSearch, cancellationToken);

        var releases = new List<ReleaseInfo>();
        var done = new HashSet<(int, string, string)>();
        foreach (var candidate in scopeCandidates.Where(c => requested.Contains(c.Episode.Id)))
        {
            if (candidate.Terms.Queries.Count == 0)
            {
                logger.LogInformation("{Series} {Episode} has no search queries", candidate.Episode.Series?.Title, candidate.Label);
                continue;
            }

            foreach (var indexer in indexers)
            {
                foreach (var query in candidate.Terms.Queries)
                {
                    var categories = candidate.Terms.Categories;
                    if (!done.Add((indexer.Id, query, string.Join(',', categories ?? []))))
                    {
                        continue;
                    }

                    var found = await FetchAsync(indexer, new TorznabSearch { Query = query, Categories = categories?.Count > 0 ? categories : null }, cancellationToken);
                    releases.AddRange(found);
                    if (found.Count > 0 && candidate.Terms.QueryMode == QueryMode.FirstWithResults)
                    {
                        break;
                    }
                }
            }
        }

        await db.Episodes.Where(e => requested.Contains(e.Id))
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.LastSearchTime, time.GetUtcNow().UtcDateTime), cancellationToken);

        var context = await ContextAsync(db, scopeCandidates, requested.ToHashSet(), indexers, userInvoked, cancellationToken);
        return (decisionMaker.GetDecisions(Distinct(releases), context), requested.Count);
    }

    /// <summary>Like Sonarr's <c>ProcessDecisions</c>: best first, never two releases for the same episode.</summary>
    private async Task<int> GrabAsync(List<DownloadDecision> decisions, CancellationToken cancellationToken)
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
        CancellationToken cancellationToken = default) => new()
        {
            Scope = candidates,
            Requested = requested,
            QualityProfiles = await db.QualityProfiles.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken),
            ReleaseProfiles = await db.ReleaseProfiles.AsNoTracking().ToListAsync(cancellationToken),
            Indexers = (indexers ?? await db.Indexers.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(i => i.Id),
            UserInvoked = userInvoked,
        };

    public static async Task<List<SearchCandidate>> LoadCandidatesAsync(GenerralDbContext db, IQueryable<Episode> episodes, CancellationToken cancellationToken = default) =>
        (await episodes.AsNoTracking().Include(e => e.Series).OrderBy(e => e.SeriesId).ThenBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToListAsync(cancellationToken))
        .Select(e => new SearchCandidate(e, SearchTerms.From(e.SearchTerms, e.Series?.SearchSettings)))
        .ToList();

    private static async Task<List<Indexer>> SearchableIndexersAsync(GenerralDbContext db, Func<Indexer, bool> enabled, CancellationToken cancellationToken) =>
        (await db.Indexers.AsNoTracking().OrderBy(i => i.Priority).ThenBy(i => i.Id).ToListAsync(cancellationToken))
        .Where(i => enabled(i) && i.Implementation == IndexerImplementations.Torznab)
        .ToList();

    private async Task<IReadOnlyList<ReleaseInfo>> FetchAsync(Indexer indexer, TorznabSearch search, CancellationToken cancellationToken)
    {
        try
        {
            return await torznab.SearchAsync(indexer, search, cancellationToken);
        }
        catch (Exception e) when (e is TorznabException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Indexer {Indexer} failed: {Message}", indexer.Name, e.Message);
            return [];
        }
    }

    private static IEnumerable<ReleaseInfo> Distinct(IEnumerable<ReleaseInfo> releases) =>
        releases.DistinctBy(r => r.InfoHash is { Length: > 0 } hash ? hash.ToUpperInvariant() : $"{r.IndexerId}:{r.Guid}");
}
