// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/DecisionEngine/DownloadDecisionComparer.cs
// and src/NzbDrone.Core/DecisionEngine/DownloadDecisionPriorizationService.cs
namespace Generral.Core.DecisionEngine;

/// <summary>
/// Higher is better: quality (profile order) → proper/repack → indexer priority → seeders → size (closer to preferred) → age (newer).
/// Release-profile scores and custom formats don't exist yet.
/// </summary>
public sealed class DownloadDecisionComparer : IComparer<DownloadDecision>
{
    public static readonly DownloadDecisionComparer Instance = new();

    private static readonly long SizeStep = AcceptableSizeSpecification.Megabytes(200);

    public int Compare(DownloadDecision? x, DownloadDecision? y)
    {
        if (x is null || y is null)
        {
            return x is null ? (y is null ? 0 : -1) : 1;
        }

        var a = x.RemoteEpisode;
        var b = y.RemoteEpisode;
        int[] results =
        [
            QualityIndex(a).CompareTo(QualityIndex(b)),
            a.Quality.Revision.CompareTo(b.Quality.Revision),
            -a.Release.IndexerPriority.CompareTo(b.Release.IndexerPriority),
            Peers(a.Release.Seeders).CompareTo(Peers(b.Release.Seeders)),
            Peers(a.Release.Peers).CompareTo(Peers(b.Release.Peers)),
            SizeScore(a).CompareTo(SizeScore(b)),
            a.Release.PublishDate.CompareTo(b.Release.PublishDate),
        ];
        return results.FirstOrDefault(r => r != 0);
    }

    /// <summary>Best first, per series (unmatched releases last).</summary>
    public static List<DownloadDecision> Prioritize(IEnumerable<DownloadDecision> decisions) =>
        decisions.GroupBy(d => d.RemoteEpisode.Series?.Id ?? 0)
            .OrderBy(g => g.Key == 0)
            .SelectMany(g => g.OrderByDescending(d => d, Instance))
            .ToList();

    /// <summary>
    /// Interactive search's default order: approved, then rejected matches, then unmatched releases; within each, best quality
    /// (in the profile's order, then revision) first, then most seeders, then the usual tie-breakers.
    /// </summary>
    public static List<DownloadDecision> InteractiveOrder(IEnumerable<DownloadDecision> decisions) =>
        decisions
            .OrderBy(d => d.Approved ? 0 : d.RemoteEpisode.Episodes.Count > 0 ? 1 : 2)
            .ThenByDescending(d => QualityIndex(d.RemoteEpisode))
            .ThenByDescending(d => d.RemoteEpisode.Quality.Revision)
            .ThenByDescending(d => d.RemoteEpisode.Release.Seeders ?? 0)
            .ThenByDescending(d => d, Instance)
            .ToList();

    public static int QualityIndex(RemoteEpisode remote) =>
        remote.QualityProfile?.GetIndex(remote.Quality.Quality.Id).Index ?? 0;

    private static double Peers(int? count) => count is > 0 ? Math.Round(Math.Log10(count.Value)) : 0;

    private static long SizeScore(RemoteEpisode remote)
    {
        var release = remote.Release;
        if (remote.QualityProfile is { } profile && profile.GetIndex(remote.Quality.Quality.Id) is var (index, groupIndex) && index >= 0)
        {
            var item = groupIndex < 0 ? profile.Items[index] : profile.Items[index].Items[groupIndex];
            var runtime = remote.Episodes.Sum(e => e.Runtime is > 0 ? e.Runtime.Value : AcceptableSizeSpecification.DefaultRuntime);
            if (item.PreferredSize is { } preferred && runtime > 0)
            {
                return -Math.Abs(Round(release.Size - (AcceptableSizeSpecification.Megabytes(preferred) * runtime)));
            }
        }

        return Round(release.Size);
    }

    private static long Round(long size) => (long)Math.Round((double)size / SizeStep) * SizeStep;
}
