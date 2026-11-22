// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/DecisionEngine/Specifications/ (ProtocolSpecification.cs,
// QualityAllowedByProfileSpecification.cs, Radarr's LanguageSpecification.cs, UpgradeDiskSpecification.cs, AcceptableSizeSpecification.cs, TorrentSeedingSpecification.cs,
// ReleaseRestrictionsSpecification.cs, RssSync/MonitoredEpisodeSpecification.cs, BlocklistSpecification.cs, QueueSpecification.cs) and src/NzbDrone.Core/Profiles/Releases/TermMatcherService.cs
using System.Globalization;
using System.Text.RegularExpressions;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.Languages;

namespace Generral.Core.DecisionEngine;

/// <summary>One check run on every release matched to episodes (spec 06 § Standard specifications).</summary>
public interface IDecisionSpecification
{
    /// <returns>The rejection reason, or null when the release is acceptable.</returns>
    string? Check(RemoteEpisode subject, DecisionContext context);
}

public sealed class ProtocolSpecification : IDecisionSpecification
{
    public string? Check(RemoteEpisode subject, DecisionContext context) =>
        subject.Release.DownloadProtocol == DownloadProtocol.Torrent ? null : $"{subject.Release.DownloadProtocol} is not supported";
}

public sealed class QualityAllowedByProfileSpecification : IDecisionSpecification
{
    public string? Check(RemoteEpisode subject, DecisionContext context)
    {
        if (subject.QualityProfile is not { } profile)
        {
            return null;
        }

        var (index, _) = profile.GetIndex(subject.Quality.Quality.Id);
        return index >= 0 && profile.Items[index].Allowed ? null : $"{subject.Quality.Quality.Name} is not wanted in profile";
    }
}

/// <summary>
/// Radarr-style: the profile's language must be among the release's. Series have no original language, so it is English:
/// untagged (Unknown) and original-language releases are English.
/// </summary>
public sealed class LanguageSpecification : IDecisionSpecification
{
    public static readonly Language OriginalLanguage = Language.English;

    public string? Check(RemoteEpisode subject, DecisionContext context)
    {
        if (subject.QualityProfile is not { } profile || profile.Language == Language.Any)
        {
            return null;
        }

        var wanted = profile.Language == Language.Original ? OriginalLanguage : profile.Language;
        var languages = subject.Languages.Select(l => l == Language.Unknown || l == Language.Original ? OriginalLanguage : l).ToList();
        return languages.Contains(wanted)
            ? null
            : $"Language {string.Join(", ", subject.Languages)} is not wanted (profile: {profile.Language.Name})";
    }
}

/// <summary>Episode files don't exist yet; until they do, an episode with a file is never upgraded.</summary>
public sealed class UpgradeDiskSpecification : IDecisionSpecification
{
    public string? Check(RemoteEpisode subject, DecisionContext context) =>
        subject.Episodes.Count > 0 && subject.Episodes.All(e => e.HasFile) ? "Existing file on disk, upgrades are not supported yet" : null;
}

public sealed class AcceptableSizeSpecification : IDecisionSpecification
{
    /// <summary>Minutes, for episodes without a runtime (spec 06: sport events).</summary>
    public const int DefaultRuntime = 120;

    public string? Check(RemoteEpisode subject, DecisionContext context)
    {
        if (subject.Release.Size == 0 || subject.QualityProfile is not { } profile || subject.Episodes.Count == 0)
        {
            return null;
        }

        var (index, groupIndex) = profile.GetIndex(subject.Quality.Quality.Id);
        if (index < 0)
        {
            return null;
        }

        var item = groupIndex < 0 ? profile.Items[index] : profile.Items[index].Items[groupIndex];
        var runtime = subject.Episodes.Sum(e => e.Runtime is > 0 ? e.Runtime.Value : DefaultRuntime);
        var runtimeMessage = subject.Episodes.Count == 1 ? $"{runtime}min" : $"{subject.Episodes.Count} episodes totalling {runtime}min";

        if (item.MinSize is { } min && subject.Release.Size < Megabytes(min) * runtime)
        {
            return $"{SizeSuffix(subject.Release.Size)} is smaller than minimum allowed {SizeSuffix(Megabytes(min) * runtime)} (for {runtimeMessage})";
        }

        if (item.MaxSize is { } max and > 0 && subject.Release.Size > Megabytes(max) * runtime)
        {
            return $"{SizeSuffix(subject.Release.Size)} is larger than maximum allowed {SizeSuffix(Megabytes(max) * runtime)} (for {runtimeMessage})";
        }

        return null;
    }

    public static long Megabytes(double megabytes) => (long)(megabytes * 1024 * 1024);

    public static string SizeSuffix(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB", "PB"];
        if (bytes <= 0)
        {
            return "0 B";
        }

        var mag = Math.Min((int)Math.Log(bytes, 1024), suffixes.Length - 1);
        return string.Format(CultureInfo.InvariantCulture, "{0:n1} {1}", bytes / Math.Pow(1024, mag), suffixes[mag]);
    }
}

public sealed class TorrentSeedingSpecification : IDecisionSpecification
{
    public string? Check(RemoteEpisode subject, DecisionContext context)
    {
        if (subject.Release.Seeders is not { } seeders || !context.Indexers.TryGetValue(subject.Release.IndexerId, out var indexer))
        {
            return null;
        }

        var minimum = TorznabSettings.From(indexer.Settings).MinimumSeeders;
        return seeders < minimum ? $"Not enough seeders: {seeders}. Minimum seeders: {minimum}" : null;
    }
}

public sealed class ReleaseRestrictionsSpecification : IDecisionSpecification
{
    public string? Check(RemoteEpisode subject, DecisionContext context)
    {
        if (subject.Series is not { } series)
        {
            return null;
        }

        var title = subject.Release.Title;
        var profiles = context.ReleaseProfiles
            .Where(p => p.Enabled && (p.IndexerId == 0 || p.IndexerId == subject.Release.IndexerId))
            .Where(p => (p.Tags.Count == 0 || p.Tags.Intersect(series.Tags).Any()) && !p.ExcludedTags.Intersect(series.Tags).Any())
            .ToList();

        foreach (var profile in profiles.Where(p => p.Required.Count > 0))
        {
            if (!profile.Required.Any(t => IsMatch(t, title)))
            {
                return $"Does not contain one of the required terms: {string.Join(", ", profile.Required)}";
            }
        }

        foreach (var profile in profiles.Where(p => p.Ignored.Count > 0))
        {
            var found = profile.Ignored.Where(t => IsMatch(t, title)).ToList();
            if (found.Count > 0)
            {
                return $"Contains these ignored terms: {string.Join(", ", found)}";
            }
        }

        return null;
    }

    /// <summary>Sonarr's term matcher: <c>/regex/i</c> or a case-insensitive substring.</summary>
    public static bool IsMatch(string term, string title)
    {
        if (term.Length > 1 && term[0] == '/' && term.LastIndexOf('/') is var end and > 0)
        {
            var options = term[(end + 1)..].Contains('i') ? RegexOptions.IgnoreCase : RegexOptions.None;
            try
            {
                return Regex.IsMatch(title, term[1..end], options | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            }
            catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException)
            {
                return false;
            }
        }

        return title.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Automatic searches and RSS only grab monitored episodes of monitored series.</summary>
public sealed class MonitoredEpisodeSpecification : IDecisionSpecification
{
    public string? Check(RemoteEpisode subject, DecisionContext context)
    {
        if (context.UserInvoked || subject.Series is not { } series)
        {
            return null;
        }

        if (!series.Monitored)
        {
            return "Series is not monitored";
        }

        return subject.Episodes.Count switch
        {
            _ when subject.Episodes.All(e => e.Monitored) => null,
            1 => "Episode is not monitored",
            _ => "One or more episodes is not monitored",
        };
    }
}

public sealed class BlocklistSpecification : IDecisionSpecification
{
    public string? Check(RemoteEpisode subject, DecisionContext context) =>
        subject.Series is { } series && context.Blocklist[series.Id].Any(b => b.Matches(subject.Release)) ? "Release is blocklisted" : null;
}

/// <summary>Without upgrades, any download in the queue for one of the episodes rejects the release.</summary>
public sealed class QueueSpecification : IDecisionSpecification
{
    public string? Check(RemoteEpisode subject, DecisionContext context) =>
        subject.Episodes.Select(e => context.QueuedEpisodes.GetValueOrDefault(e.Id)).OfType<string>().FirstOrDefault() is { } title
            ? $"Release in queue already: {title}"
            : null;
}
