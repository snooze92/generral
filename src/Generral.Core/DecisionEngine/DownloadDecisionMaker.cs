// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/DecisionEngine/DownloadDecisionMaker.cs
using Generral.Core.Indexers;
using Generral.Core.Qualities;

namespace Generral.Core.DecisionEngine;

/// <summary>
/// Matches releases to episodes with the mappings' rules (spec 06 § Matching), then runs the standard specifications.
/// Every rejection is kept, so interactive search can show all of them.
/// </summary>
public sealed class DownloadDecisionMaker(TimeProvider time)
{
    private static readonly IDecisionSpecification[] Specifications =
    [
        new ProtocolSpecification(),
        new QualityAllowedByProfileSpecification(),
        new UpgradeDiskSpecification(),
        new AcceptableSizeSpecification(),
        new TorrentSeedingSpecification(),
        new ReleaseRestrictionsSpecification(),
        new MonitoredEpisodeSpecification(),
    ];

    public List<DownloadDecision> GetDecisions(IEnumerable<ReleaseInfo> releases, DecisionContext context) =>
        releases.Select(r => GetDecision(r, context)).ToList();

    public DownloadDecision GetDecision(ReleaseInfo release, DecisionContext context)
    {
        var remote = new RemoteEpisode { Release = release, Quality = QualityParser.ParseQuality(release.Title) };
        var rejections = Match(remote, context);
        return Evaluate(remote, context, rejections);
    }

    /// <summary>Runs the specifications for a release whose episodes are already set (e.g. assigned by the user).</summary>
    public DownloadDecision Evaluate(RemoteEpisode remote, DecisionContext context, List<string>? rejections = null)
    {
        rejections ??= [];
        if (remote.Series is { } series)
        {
            remote.QualityProfile ??= context.QualityProfiles.GetValueOrDefault(series.QualityProfileId);
        }

        if (remote.Episodes.Count > 0)
        {
            rejections.AddRange(Specifications.Select(s => s.Check(remote, context)).OfType<string>());
        }

        return new DownloadDecision(remote, rejections);
    }

    private List<string> Match(RemoteEpisode remote, DecisionContext context)
    {
        var release = remote.Release;
        var titleMatches = new List<SearchCandidate>();
        var misses = new List<(SearchCandidate Candidate, string Reason)>();
        foreach (var candidate in context.Scope)
        {
            var reason = candidate.HasRules ? ReleaseTitleMatcher.Check(candidate.Terms, release.Title) : "No search rules (must/any)";
            if (reason is null)
            {
                titleMatches.Add(candidate);
            }
            else
            {
                misses.Add((candidate, reason));
            }
        }

        var inWindow = titleMatches.Where(c => DateWindowRejection(c, release) is null).ToList();
        var matches = inWindow.Count > 0 ? inWindow : titleMatches;
        remote.Episodes = matches.Select(c => c.Episode).ToList();
        remote.Series = matches.FirstOrDefault()?.Episode.Series
            ?? context.Scope.FirstOrDefault(c => context.Requested?.Contains(c.Episode.Id) == true)?.Episode.Series;
        remote.EpisodeRequested = context.Requested is null || remote.Episodes.Any(e => context.Requested.Contains(e.Id));

        if (matches.Count == 0)
        {
            var requested = misses.Where(m => context.Requested?.Contains(m.Candidate.Episode.Id) == true).ToList();
            return requested.Count == 1
                ? [$"Does not match {requested[0].Candidate.Label}: {requested[0].Reason}"]
                : ["Does not match any wanted episode"];
        }

        if (matches.Count > 1)
        {
            return [$"Ambiguous: matches {string.Join(", ", matches.Select(c => c.Label))}"];
        }

        var rejections = new List<string>();
        if (DateWindowRejection(matches[0], release) is { } window)
        {
            rejections.Add(window);
        }

        if (!remote.EpisodeRequested)
        {
            rejections.Add($"Matches {matches[0].Label}, which wasn't requested");
        }

        return rejections;
    }

    private string? DateWindowRejection(SearchCandidate candidate, ReleaseInfo release)
    {
        if (candidate.Episode.AirDateUtc is not { } air)
        {
            return null;
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (air - candidate.Terms.Before > now)
        {
            return $"{candidate.Label} has not aired yet (airs {air:yyyy-MM-dd HH:mm} UTC)";
        }

        if (release.PublishDate != default &&
            (release.PublishDate < air - candidate.Terms.Before || release.PublishDate > air + candidate.Terms.After))
        {
            return $"Published {release.PublishDate:yyyy-MM-dd}, outside the date window of {candidate.Label} (aired {air:yyyy-MM-dd})";
        }

        return null;
    }
}
