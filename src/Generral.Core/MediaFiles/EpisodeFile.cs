// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/MediaFiles/EpisodeFile.cs
using Generral.Core.Languages;
using Generral.Core.Qualities;

namespace Generral.Core.MediaFiles;

/// <summary>A video file in the library; the episodes it covers point at it with <c>Episode.EpisodeFileId</c> (several for a multi-episode file).</summary>
public class EpisodeFile
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public int SeasonNumber { get; set; }

    /// <summary>Relative to the series folder.</summary>
    public required string RelativePath { get; set; }

    public required string Path { get; set; }

    public long Size { get; set; }

    public DateTime DateAdded { get; set; }

    /// <summary>The original release title.</summary>
    public string? SceneName { get; set; }

    public string? ReleaseGroup { get; set; }

    public QualityModel Quality { get; set; } = new();

    public List<Language> Languages { get; set; } = [];

    /// <summary>The file name before the import (Sonarr's <c>OriginalFilePath</c>), for <c>{Original Filename}</c>.</summary>
    public string? OriginalFilePath { get; set; }
}
