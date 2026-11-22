// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/DecisionEngine/DownloadDecision.cs and src/NzbDrone.Core/Parser/Model/RemoteEpisode.cs
using Generral.Core.Blocklisting;
using Generral.Core.Indexers;
using Generral.Core.Languages;
using Generral.Core.MediaFiles;
using Generral.Core.Profiles;
using Generral.Core.Qualities;
using Generral.Core.Tv;

namespace Generral.Core.DecisionEngine;

/// <summary>A release with what Generral made of it: the episodes it matched and its quality.</summary>
public sealed class RemoteEpisode
{
    public required ReleaseInfo Release { get; init; }

    public required QualityModel Quality { get; init; }

    /// <summary>Parsed from the title (without the text matched by the episode's rules); <see cref="Language.Unknown"/> when untagged, <see cref="Language.Original"/> for MULTi releases.</summary>
    public IReadOnlyList<Language> Languages { get; set; } = [Language.Unknown];

    /// <summary>The matched series, or the searched one when nothing matched; null for unmatched RSS releases.</summary>
    public Series? Series { get; set; }

    public QualityProfile? QualityProfile { get; set; }

    public List<Episode> Episodes { get; set; } = [];

    /// <summary>The matched episodes are among those searched for.</summary>
    public bool EpisodeRequested { get; set; }

    public ReleaseSourceType ReleaseSource { get; set; }

    public override string ToString() => Release.Title;
}

public enum ReleaseSourceType
{
    Unknown,
    Rss,
    Search,
    UserInvokedSearch,
    InteractiveSearch,
    ReleasePush,
}

public sealed record DownloadDecision(RemoteEpisode RemoteEpisode, IReadOnlyList<string> Rejections)
{
    public bool Approved => Rejections.Count == 0;
}

/// <summary>An episode that releases are matched against, with its resolved search settings.</summary>
public sealed record SearchCandidate(Episode Episode, SearchTerms Terms)
{
    /// <summary>Without <c>must</c> or <c>any</c> rules every title would match, so such episodes never do.</summary>
    public bool HasRules => Terms.Must.Count > 0 || Terms.Any.Count > 0;

    public string Label => Format(Episode);

    public static string Format(Episode episode) => $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
}

/// <summary>Everything the decision engine needs, loaded once per search or RSS sync.</summary>
public sealed record DecisionContext
{
    /// <summary>Episodes a release may match (with <c>Episode.Series</c> loaded); matching several is ambiguous.</summary>
    public required IReadOnlyList<SearchCandidate> Scope { get; init; }

    /// <summary>The searched episodes, or null when every episode in scope is wanted (RSS).</summary>
    public IReadOnlySet<int>? Requested { get; init; }

    public required IReadOnlyDictionary<int, QualityProfile> QualityProfiles { get; init; }

    public IReadOnlyList<ReleaseProfile> ReleaseProfiles { get; init; } = [];

    public IReadOnlyDictionary<int, Indexer> Indexers { get; init; } = new Dictionary<int, Indexer>();

    /// <summary>A user search: unmonitored episodes may be grabbed.</summary>
    public bool UserInvoked { get; init; }

    /// <summary>Blocklist entries of the series in scope, by series id.</summary>
    public ILookup<int, Blocklist> Blocklist { get; init; } = Array.Empty<Blocklist>().ToLookup(b => b.SeriesId);

    /// <summary>Existing files of the episodes in scope, by id.</summary>
    public IReadOnlyDictionary<int, EpisodeFile> EpisodeFiles { get; init; } = new Dictionary<int, EpisodeFile>();

    /// <summary>Episodes with a download in the queue, and its title.</summary>
    public IReadOnlyDictionary<int, string> QueuedEpisodes { get; init; } = new Dictionary<int, string>();
}
