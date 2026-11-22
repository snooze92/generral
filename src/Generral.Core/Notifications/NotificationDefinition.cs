// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Notifications/NotificationDefinition.cs, INotification.cs, GrabMessage.cs,
// DownloadMessage.cs, EpisodeDeleteMessage.cs, SeriesAddMessage.cs and SeriesDeleteMessage.cs
using System.Text.Json.Nodes;
using Generral.Core.DecisionEngine;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.MediaFiles;
using Generral.Core.Tv;

namespace Generral.Core.Notifications;

/// <summary>One connection (Settings → Connect) with its triggers.</summary>
public class NotificationDefinition
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Implementation { get; set; }

    public JsonObject Settings { get; set; } = [];

    public List<int> Tags { get; set; } = [];

    public bool OnGrab { get; set; }

    public bool OnDownload { get; set; }

    public bool OnUpgrade { get; set; }

    public bool OnSeriesAdd { get; set; }

    public bool OnSeriesDelete { get; set; }

    public bool OnEpisodeFileDelete { get; set; }

    public bool OnEpisodeFileDeleteForUpgrade { get; set; }

    public bool Has(NotificationTrigger trigger) => trigger switch
    {
        NotificationTrigger.Grab => OnGrab,
        NotificationTrigger.Download => OnDownload,
        NotificationTrigger.Upgrade => OnUpgrade,
        NotificationTrigger.SeriesAdd => OnSeriesAdd,
        NotificationTrigger.SeriesDelete => OnSeriesDelete,
        NotificationTrigger.EpisodeFileDelete => OnEpisodeFileDelete,
        NotificationTrigger.EpisodeFileDeleteForUpgrade => OnEpisodeFileDeleteForUpgrade,
        _ => false,
    };
}

/// <summary>Sonarr's triggers that Generral raises; rename and health triggers come with those features.</summary>
public enum NotificationTrigger
{
    Grab,
    Download,
    Upgrade,
    SeriesAdd,
    SeriesDelete,
    EpisodeFileDelete,
    EpisodeFileDeleteForUpgrade,
}

/// <summary>An event to notify; episodes are already spoiler-filtered.</summary>
public abstract record NotificationMessage
{
    public required Series Series { get; init; }

    public IReadOnlyList<Episode> Episodes { get; init; } = [];
}

public sealed record GrabMessage : NotificationMessage
{
    public required RemoteEpisode Remote { get; init; }

    public required string DownloadClientName { get; init; }

    public required string DownloadClientType { get; init; }

    public required string DownloadId { get; init; }
}

/// <summary>The release a download came from, as recorded at grab time.</summary>
public sealed record GrabbedRelease(string? Title, string? Indexer, long? Size);

public sealed record DownloadMessage : NotificationMessage
{
    public required EpisodeFile File { get; init; }

    public IReadOnlyList<EpisodeFile> OldFiles { get; init; } = [];

    public required string SourcePath { get; init; }

    public string? DownloadClientName { get; init; }

    public string? DownloadClientType { get; init; }

    public string? DownloadId { get; init; }

    public GrabbedRelease? Release { get; init; }

    public bool IsUpgrade => OldFiles.Count > 0;
}

public sealed record EpisodeFileDeleteMessage : NotificationMessage
{
    public required EpisodeFile File { get; init; }

    /// <summary><c>Manual</c> or <c>Upgrade</c> (Sonarr's <c>DeleteMediaFileReason</c>).</summary>
    public required string Reason { get; init; }
}

public sealed record SeriesAddMessage : NotificationMessage;

public sealed record SeriesDeleteMessage : NotificationMessage
{
    public bool DeletedFiles { get; init; }
}

/// <summary>One connection type; stateless, the definition is passed to every call.</summary>
public interface INotification
{
    string Implementation { get; }

    string Name { get; }

    string Link { get; }

    IReadOnlyList<IndexerField> Fields { get; }

    IReadOnlySet<NotificationTrigger> SupportedTriggers { get; }

    /// <exception cref="NotificationException">Sending failed.</exception>
    Task SendAsync(NotificationDefinition definition, NotificationMessage message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexerValidationFailure>> TestAsync(NotificationDefinition definition, CancellationToken cancellationToken = default);

    IReadOnlyList<IndexerValidationFailure> Validate(JsonObject settings);
}

/// <summary>A connection that refreshes a media server library; also told when only metadata files changed.</summary>
public interface IMediaServerNotification : INotification
{
    /// <exception cref="NotificationException">The update failed.</exception>
    Task UpdateLibraryAsync(NotificationDefinition definition, Series series, CancellationToken cancellationToken = default);
}

public class NotificationException(string message, Exception? inner = null) : Exception(message, inner);
