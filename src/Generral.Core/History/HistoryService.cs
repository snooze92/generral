// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/History/HistoryService.cs
using System.Globalization;
using Generral.Core.Datastore;
using Generral.Core.DecisionEngine;
using Generral.Core.Download;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.History;

public sealed class HistoryService(GenerralDbContext db, TimeProvider time)
{
    /// <summary>One <c>grabbed</c> row per episode, with Sonarr's data keys.</summary>
    public async Task RecordGrabbedAsync(RemoteEpisode remote, DownloadClient client, string downloadId, CancellationToken cancellationToken = default)
    {
        var release = remote.Release;
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var episode in remote.Episodes)
        {
            db.History.Add(new EpisodeHistory
            {
                EpisodeId = episode.Id,
                SeriesId = episode.SeriesId,
                SourceTitle = release.Title,
                Quality = remote.Quality,
                Date = now,
                EventType = EpisodeHistoryEventType.Grabbed,
                DownloadId = downloadId,
                Data = new Dictionary<string, string?>
                {
                    [EpisodeHistory.Indexer] = release.Indexer,
                    ["indexerId"] = release.IndexerId.ToString(CultureInfo.InvariantCulture),
                    ["nzbInfoUrl"] = release.InfoUrl,
                    [EpisodeHistory.PublishedDate] = release.PublishDate == default ? null : release.PublishDate.ToString("s", CultureInfo.InvariantCulture) + "Z",
                    [EpisodeHistory.DownloadClient] = client.Implementation,
                    [EpisodeHistory.DownloadClientName] = client.Name,
                    ["downloadClientId"] = client.Id.ToString(CultureInfo.InvariantCulture),
                    [EpisodeHistory.Size] = release.Size.ToString(CultureInfo.InvariantCulture),
                    ["downloadUrl"] = release.DownloadUrl ?? release.MagnetUrl,
                    ["guid"] = release.Guid,
                    [EpisodeHistory.Protocol] = ((int)release.DownloadProtocol).ToString(CultureInfo.InvariantCulture),
                    [EpisodeHistory.ReleaseSource] = remote.ReleaseSource.ToString(),
                    ["indexerFlags"] = release.IndexerFlags.ToString(),
                    [EpisodeHistory.TorrentInfoHash] = downloadId,
                },
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The grab of a download, newest first (one row per episode).</summary>
    public async Task<List<EpisodeHistory>> GrabbedAsync(string downloadId, CancellationToken cancellationToken = default) =>
        (await db.History.AsNoTracking()
            .Where(h => h.DownloadId == downloadId && h.EventType == EpisodeHistoryEventType.Grabbed)
            .ToListAsync(cancellationToken))
        .OrderByDescending(h => h.Date).ThenBy(h => h.Id)
        .ToList();

    public async Task<bool> HasEventAsync(string downloadId, EpisodeHistoryEventType eventType, CancellationToken cancellationToken = default) =>
        await db.History.AnyAsync(h => h.DownloadId == downloadId && h.EventType == eventType, cancellationToken);

    /// <summary>Writes one row per grabbed episode for a failed or ignored download.</summary>
    public async Task RecordAsync(IReadOnlyList<EpisodeHistory> grabbed, EpisodeHistoryEventType eventType, string message, string? source, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var grab in grabbed.DistinctBy(h => h.EpisodeId))
        {
            db.History.Add(new EpisodeHistory
            {
                EpisodeId = grab.EpisodeId,
                SeriesId = grab.SeriesId,
                SourceTitle = grab.SourceTitle,
                Quality = grab.Quality,
                Date = now,
                EventType = eventType,
                DownloadId = grab.DownloadId,
                Data = new Dictionary<string, string?>
                {
                    [EpisodeHistory.DownloadClient] = grab.Data.GetValueOrDefault(EpisodeHistory.DownloadClient),
                    [EpisodeHistory.DownloadClientName] = grab.Data.GetValueOrDefault(EpisodeHistory.DownloadClientName),
                    [EpisodeHistory.Message] = message,
                    ["source"] = source,
                    [EpisodeHistory.Indexer] = grab.Data.GetValueOrDefault(EpisodeHistory.Indexer),
                    [EpisodeHistory.Size] = grab.Data.GetValueOrDefault(EpisodeHistory.Size),
                    [EpisodeHistory.ReleaseSource] = grab.Data.GetValueOrDefault(EpisodeHistory.ReleaseSource),
                },
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
