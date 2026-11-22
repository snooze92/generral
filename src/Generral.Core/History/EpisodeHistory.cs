// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/History/EpisodeHistory.cs
using Generral.Core.Qualities;

namespace Generral.Core.History;

public enum EpisodeHistoryEventType
{
    Unknown,
    Grabbed,
    SeriesFolderImported,
    DownloadFolderImported,
    DownloadFailed,
    EpisodeFileDeleted,
    EpisodeFileRenamed,
    DownloadIgnored,
}

/// <summary>One event for one episode; a grab of a multi-episode release writes one row per episode.</summary>
public class EpisodeHistory
{
    public const string Indexer = "indexer";
    public const string DownloadClient = "downloadClient";
    public const string DownloadClientName = "downloadClientName";
    public const string Size = "size";
    public const string PublishedDate = "publishedDate";
    public const string Protocol = "protocol";
    public const string ReleaseSource = "releaseSource";
    public const string TorrentInfoHash = "torrentInfoHash";
    public const string Message = "message";

    public int Id { get; set; }

    public int EpisodeId { get; set; }

    public int SeriesId { get; set; }

    public required string SourceTitle { get; set; }

    public QualityModel Quality { get; set; } = new();

    public DateTime Date { get; set; }

    public EpisodeHistoryEventType EventType { get; set; }

    /// <summary>Sonarr's free-form data, with camelCase keys as the API returns them.</summary>
    public Dictionary<string, string?> Data { get; set; } = [];

    public string? DownloadId { get; set; }
}
