// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Extras/Metadata/MetadataDefinition.cs, MetadataType.cs,
// Files/MetadataFile.cs, Files/MetadataFileResult.cs, Files/ImageFileResult.cs and src/NzbDrone.Core/Extras/Files/ExtraFile.cs
using System.Text.Json.Nodes;

namespace Generral.Core.Extras.Metadata;

/// <summary>One metadata consumer's configuration (Settings → Metadata).</summary>
public class MetadataDefinition
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Implementation { get; set; }

    public bool Enable { get; set; }

    public JsonObject Settings { get; set; } = [];
}

public enum MetadataType
{
    Unknown,
    SeriesMetadata,
    EpisodeMetadata,
    SeriesImage,
    SeasonImage,
    EpisodeImage,
}

/// <summary>A file a consumer wrote next to the media, so it can be rewritten when it changes and removed with its video.</summary>
public class MetadataFile
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public int? EpisodeFileId { get; set; }

    public int? SeasonNumber { get; set; }

    /// <summary>Relative to the series folder.</summary>
    public required string RelativePath { get; set; }

    public required string Consumer { get; set; }

    public MetadataType Type { get; set; }

    /// <summary>Hash of the written text, or the source URL of an image.</summary>
    public string? Hash { get; set; }

    public DateTime Added { get; set; }

    public DateTime LastUpdated { get; set; }
}

public sealed record MetadataFileResult(string RelativePath, string Contents);

public sealed record ImageFileResult(string RelativePath, string Url);
