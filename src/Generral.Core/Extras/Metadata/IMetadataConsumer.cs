// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Extras/Metadata/IMetadata.cs
using System.Text.Json.Nodes;
using Generral.Core.Indexers;
using Generral.Core.MediaFiles;
using Generral.Core.Tv;

namespace Generral.Core.Extras.Metadata;

/// <summary>The series and episodes a consumer writes; episodes are already spoiler-filtered.</summary>
public sealed record MetadataSeries(Series Series, IReadOnlyList<Season> Seasons, DateOnly? Premiered);

/// <summary>One metadata format; stateless, the settings are passed to every call. Paths are relative to the series folder.</summary>
public interface IMetadataConsumer
{
    string Implementation { get; }

    string Name { get; }

    IReadOnlyList<IndexerField> Fields { get; }

    MetadataFileResult? SeriesMetadata(MetadataSeries series, JsonObject settings);

    MetadataFileResult? EpisodeMetadata(EpisodeFile file, IReadOnlyList<Episode> episodes, JsonObject settings);

    IEnumerable<ImageFileResult> SeriesImages(Series series, JsonObject settings);

    /// <param name="seasonFolder">The season's folder relative to the series folder, empty without season folders.</param>
    IEnumerable<ImageFileResult> SeasonImages(Season season, string seasonFolder, JsonObject settings);

    IEnumerable<ImageFileResult> EpisodeImages(EpisodeFile file, IReadOnlyList<Episode> episodes, JsonObject settings);
}
