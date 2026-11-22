// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownload.cs and TrackedDownloadStatusMessage.cs
using System.Security.Cryptography;
using System.Text;
using Generral.Core.Indexers;
using Generral.Core.Qualities;

namespace Generral.Core.Download.TrackedDownloads;

public enum TrackedDownloadState
{
    Downloading,
    ImportBlocked,
    ImportPending,
    Importing,
    Imported,
    FailedPending,
    Failed,
    Ignored,
}

public enum TrackedDownloadStatus
{
    Ok,
    Warning,
    Error,
}

public sealed record TrackedDownloadStatusMessage(string Title, IReadOnlyList<string> Messages);

/// <summary>A download in a client, linked to its grab (by download id) when Generral grabbed it.</summary>
public sealed class TrackedDownload
{
    public required int DownloadClientId { get; init; }

    public required string DownloadClientName { get; set; }

    /// <summary>Upper-case infohash.</summary>
    public required string DownloadId { get; init; }

    /// <summary>The client's view; null until the first refresh after the grab.</summary>
    public DownloadClientItem? Item { get; set; }

    public TrackedDownloadState State { get; set; }

    public TrackedDownloadStatus Status { get; set; }

    public List<TrackedDownloadStatusMessage> StatusMessages { get; set; } = [];

    /// <summary>Null for downloads Generral didn't grab (shown as unknown in the queue).</summary>
    public int? SeriesId { get; set; }

    public List<int> EpisodeIds { get; set; } = [];

    public required string Title { get; set; }

    public QualityModel Quality { get; set; } = new();

    public string? Indexer { get; set; }

    /// <summary>The grabbed release's size, until the client reports the download.</summary>
    public long Size { get; set; }

    public DownloadProtocol Protocol { get; set; } = DownloadProtocol.Torrent;

    public DateTime? Added { get; set; }

    /// <summary>When the client first reported the download stalled (in memory, reset on restart).</summary>
    public DateTime? StalledSince { get; set; }

    /// <summary>Sonarr's queue id: a stable hash of the client and download id.</summary>
    public int QueueId => QueueIdFor(DownloadClientId, DownloadId);

    public static int QueueIdFor(int downloadClientId, string downloadId)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes($"trackedDownload-{downloadClientId}-{downloadId}"));
        return BitConverter.ToInt32(hash, 0) & 0x7fffffff;
    }

    public void Warn(string message)
    {
        Status = TrackedDownloadStatus.Warning;
        StatusMessages = [new TrackedDownloadStatusMessage(Item?.Title ?? Title, [message])];
    }
}
