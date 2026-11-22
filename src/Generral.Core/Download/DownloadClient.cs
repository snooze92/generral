// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Download/DownloadClientDefinition.cs, src/NzbDrone.Core/Download/DownloadClientItem.cs,
// src/NzbDrone.Core/Download/IDownloadClient.cs and src/NzbDrone.Core/Download/Clients/DownloadClientException.cs
using System.Text.Json.Nodes;
using Generral.Core.DecisionEngine;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;

namespace Generral.Core.Download;

public class DownloadClient
{
    public const int DefaultPriority = 1;

    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Implementation { get; set; }

    /// <summary>Settings keyed by Sonarr field name (e.g. <c>tvCategory</c>).</summary>
    public JsonObject Settings { get; set; } = [];

    public bool Enable { get; set; } = true;

    /// <summary>1 (highest) to 50.</summary>
    public int Priority { get; set; } = DefaultPriority;

    public bool RemoveCompletedDownloads { get; set; } = true;

    public bool RemoveFailedDownloads { get; set; } = true;

    public List<int> Tags { get; set; } = [];
}

public enum DownloadItemStatus
{
    Queued,
    Paused,
    Downloading,
    Completed,
    Failed,
    Warning,
}

/// <summary>A download as the client reports it (spec 07 § Abstraction).</summary>
public sealed record DownloadClientItem
{
    /// <summary>Upper-case infohash for torrents.</summary>
    public required string DownloadId { get; init; }

    public required string Title { get; init; }

    public string? Category { get; init; }

    public long TotalSize { get; init; }

    public long RemainingSize { get; init; }

    public TimeSpan? RemainingTime { get; init; }

    public DownloadItemStatus Status { get; init; }

    public string? Message { get; init; }

    public string? OutputPath { get; init; }

    public bool CanMoveFiles { get; init; }

    public bool CanBeRemoved { get; init; }

    public double? SeedRatio { get; init; }

    /// <summary>Downloading, but without connections (qBittorrent <c>stalledDL</c>); see stalled handling.</summary>
    public bool IsStalled { get; init; }
}

/// <summary>One download client implementation; stateless, the definition is passed to every call.</summary>
public interface IDownloadClient
{
    string Implementation { get; }

    DownloadProtocol Protocol { get; }

    /// <summary>Adds the torrent; seed criteria come from the indexer.</summary>
    /// <returns>The download id (upper-case infohash).</returns>
    /// <exception cref="DownloadClientException">The client failed.</exception>
    /// <exception cref="ReleaseDownloadException">The client rejected the release.</exception>
    Task<string> AddTorrentAsync(DownloadClient client, TorrentPayload torrent, RemoteEpisode remoteEpisode, Indexer? indexer, CancellationToken cancellationToken = default);

    /// <exception cref="DownloadClientException">The client failed.</exception>
    Task<IReadOnlyList<DownloadClientItem>> GetItemsAsync(DownloadClient client, CancellationToken cancellationToken = default);

    /// <exception cref="DownloadClientException">The client failed.</exception>
    Task RemoveItemAsync(DownloadClient client, string downloadId, bool deleteData, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexerValidationFailure>> TestAsync(DownloadClient client, CancellationToken cancellationToken = default);

    /// <returns>Validation errors of the settings alone.</returns>
    IReadOnlyList<IndexerValidationFailure> Validate(JsonObject settings);
}

public class DownloadClientException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class DownloadClientAuthenticationException(string message, Exception? inner = null) : DownloadClientException(message, inner);

/// <summary>The release couldn't be sent (fetch failed, blocklisted, rejected by the client).</summary>
public class ReleaseDownloadException(string message, Exception? inner = null) : Exception(message, inner);
