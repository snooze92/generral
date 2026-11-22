// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Download/DownloadService.cs and src/NzbDrone.Core/Download/TorrentClientBase.cs
using Generral.Core.Blocklisting;
using Generral.Core.Datastore;
using Generral.Core.DecisionEngine;
using Generral.Core.Download.TrackedDownloads;
using Generral.Core.History;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Download;

/// <summary>Sends a release to a download client.</summary>
public interface IDownloadService
{
    /// <returns>The download id.</returns>
    /// <exception cref="DownloadClientException">No usable download client, or it failed.</exception>
    /// <exception cref="ReleaseDownloadException">The release couldn't be fetched, is blocklisted, or was rejected.</exception>
    Task<string> GrabAsync(RemoteEpisode remoteEpisode, CancellationToken cancellationToken = default);
}

/// <summary>Grab: pick a client, fetch the torrent, add it, record the grab in history and track it in the queue.</summary>
public sealed class DownloadService(
    IServiceScopeFactory scopes,
    DownloadClientProvider provider,
    TorrentReleaseFetcher fetcher,
    TrackedDownloadService tracked,
    ILogger<DownloadService> logger) : IDownloadService
{
    public async Task<string> GrabAsync(RemoteEpisode remoteEpisode, CancellationToken cancellationToken = default)
    {
        var release = remoteEpisode.Release;
        if (remoteEpisode.Episodes.Count == 0)
        {
            throw new ReleaseDownloadException($"{release.Title} isn't matched to any episode");
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var indexer = release.IndexerId > 0 ? await db.Indexers.AsNoTracking().SingleOrDefaultAsync(i => i.Id == release.IndexerId, cancellationToken) : null;
        var clients = await db.DownloadClients.AsNoTracking().ToListAsync(cancellationToken);
        var client = provider.Select(clients, release.DownloadProtocol, indexer, remoteEpisode.Series?.Tags ?? []);
        var implementation = provider.Find(client.Implementation)!;

        var torrent = await fetcher.ResolveAsync(release, cancellationToken);
        var seriesId = remoteEpisode.Series?.Id ?? remoteEpisode.Episodes[0].SeriesId;
        if (string.IsNullOrWhiteSpace(release.InfoHash) && remoteEpisode.ReleaseSource != ReleaseSourceType.InteractiveSearch &&
            indexer is not null && TorznabSettings.From(indexer.Settings).RejectBlocklistedTorrentHashesWhileGrabbing &&
            await scope.ServiceProvider.GetRequiredService<BlocklistService>().IsHashBlocklistedAsync(seriesId, torrent.Hash, cancellationToken))
        {
            throw new ReleaseDownloadException("Release previously added to blocklist");
        }

        var downloadId = await implementation.AddTorrentAsync(client, torrent, remoteEpisode, indexer, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<HistoryService>().RecordGrabbedAsync(remoteEpisode, client, downloadId, cancellationToken);
        tracked.TrackGrab(client, downloadId, remoteEpisode);
        if ((remoteEpisode.Series ?? await db.Series.AsNoTracking().SingleOrDefaultAsync(s => s.Id == seriesId, cancellationToken)) is { } series)
        {
            await scope.ServiceProvider.GetRequiredService<NotificationService>().NotifyAsync(NotificationTrigger.Grab, new GrabMessage
            {
                Series = series,
                Episodes = remoteEpisode.Episodes,
                Remote = remoteEpisode,
                DownloadClientName = client.Name,
                DownloadClientType = client.Implementation,
                DownloadId = downloadId,
            }, cancellationToken);
        }

        logger.LogInformation("Report sent to {Client} from indexer {Indexer}. {Title}", client.Name, release.Indexer, release.Title);
        return downloadId;
    }
}
