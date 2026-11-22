// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/DecisionEngine/Specifications/UpgradableSpecification.cs (without custom formats)
using Generral.Core.Profiles;
using Generral.Core.Qualities;

namespace Generral.Core.DecisionEngine;

public enum UpgradeableRejectReason
{
    None,
    BetterQuality,
    BetterRevision,
    QualityCutoff,
    UpgradesNotAllowed,
}

/// <summary>Quality comparisons in profile order (higher index = better); propers and repacks are preferred, as in Sonarr's default.</summary>
public static class UpgradableSpecification
{
    /// <summary>Qualities in the same group are equal.</summary>
    public static int Compare(QualityProfile profile, Quality left, Quality right) =>
        profile.GetIndex(left.Id).Index.CompareTo(profile.GetIndex(right.Id).Index);

    /// <summary>The cutoff item's id, or the first allowed one when upgrades are off.</summary>
    public static int Cutoff(QualityProfile profile) =>
        profile.UpgradeAllowed ? profile.Cutoff : FirstAllowed(profile) is { } first ? first.Quality?.Id ?? first.Id : profile.Cutoff;

    public static string CutoffName(QualityProfile profile)
    {
        var (index, _) = profile.GetIndex(Cutoff(profile));
        return index < 0 ? "Unknown" : profile.Items[index].Name ?? profile.Items[index].Quality?.Name ?? "Unknown";
    }

    public static bool QualityCutoffNotMet(QualityProfile profile, QualityModel current, QualityModel? candidate = null)
    {
        var (currentIndex, _) = profile.GetIndex(current.Quality.Id);
        var (cutoffIndex, _) = profile.GetIndex(Cutoff(profile));
        return currentIndex < cutoffIndex || (candidate is not null && IsRevisionUpgrade(current, candidate));
    }

    public static bool IsRevisionUpgrade(QualityModel current, QualityModel candidate) =>
        current.Quality == candidate.Quality && candidate.Revision.CompareTo(current.Revision) > 0;

    public static UpgradeableRejectReason IsUpgradable(QualityProfile profile, QualityModel current, QualityModel candidate)
    {
        var qualityCompare = Compare(profile, candidate.Quality, current.Quality);
        if (qualityCompare > 0 && QualityCutoffNotMet(profile, current, candidate))
        {
            return UpgradeableRejectReason.None;
        }

        if (qualityCompare < 0)
        {
            return UpgradeableRejectReason.BetterQuality;
        }

        var revisionCompare = candidate.Revision.CompareTo(current.Revision);
        if (revisionCompare > 0)
        {
            return UpgradeableRejectReason.None;
        }

        if (!profile.UpgradeAllowed)
        {
            return UpgradeableRejectReason.UpgradesNotAllowed;
        }

        if (revisionCompare < 0)
        {
            return UpgradeableRejectReason.BetterRevision;
        }

        // Without custom formats an equal quality and revision is never an upgrade.
        return qualityCompare > 0 ? UpgradeableRejectReason.QualityCutoff : UpgradeableRejectReason.BetterQuality;
    }

    /// <returns>Sonarr's rejection message for a release (or file) of <paramref name="candidate"/> against an existing file, or null.</returns>
    public static string? Rejection(QualityProfile profile, QualityModel existing, QualityModel candidate)
    {
        if (!QualityCutoffNotMet(profile, existing, candidate))
        {
            return $"Existing file meets cutoff: {CutoffName(profile)}";
        }

        return IsUpgradable(profile, existing, candidate) switch
        {
            UpgradeableRejectReason.BetterQuality => $"Existing file on disk is of equal or higher preference: {existing}",
            UpgradeableRejectReason.BetterRevision => $"Existing file on disk is of equal or higher revision: {existing.Revision}",
            UpgradeableRejectReason.QualityCutoff => $"Existing file on disk meets quality cutoff: {CutoffName(profile)}",
            UpgradeableRejectReason.UpgradesNotAllowed => $"Existing file on disk and Quality Profile '{profile.Name}' does not allow upgrades",
            _ => null,
        };
    }

    private static QualityProfileQualityItem? FirstAllowed(QualityProfile profile) => profile.Items.FirstOrDefault(i => i.Allowed);
}
