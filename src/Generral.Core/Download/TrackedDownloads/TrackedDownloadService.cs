// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownloadService.cs, DownloadMonitoringService.cs,
// src/NzbDrone.Core/Download/FailedDownloadService.cs, RedownloadFailedDownloadService.cs and DownloadEventHub.cs
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Generral.Core.Blocklisting;
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.DecisionEngine;
using Generral.Core.History;
using Generral.Core.IndexerSearch;
using Generral.Core.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Download.TrackedDownloads;

public sealed record RefreshResult(int Tracked, int Failed, int Imported, IReadOnlyList<IReadOnlyList<int>> Redownload);

/// <summary>
/// The downloads of every enabled client (in memory, rebuilt from the clients and the grab history), their state, and failed
/// download handling: failed or stalled grabs are recorded in history, blocklisted and searched again.
/// </summary>
public sealed class TrackedDownloadService(IServiceScopeFactory scopes, DownloadClientProvider provider, TimeProvider time, ILogger<TrackedDownloadService> logger)
{
    public const string FailedDownloadHandling = "Generral Failed Download Handling";

    /// <summary>A stalled download shows a warning after this long without connections.</summary>
    public static readonly TimeSpan StalledWarningAfter = TimeSpan.FromHours(1);

    /// <summary>A fresh grab that the client doesn't list yet is kept this long.</summary>
    public static readonly TimeSpan UnseenGrace = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<(int ClientId, string DownloadId), TrackedDownload> _tracked = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The queue: every tracked download except ignored and imported ones, soonest finished first.</summary>
    public IReadOnlyList<TrackedDownload> Queue() =>
        _tracked.Values.Where(t => t.State is not (TrackedDownloadState.Ignored or TrackedDownloadState.Imported))
            .OrderBy(t => t.Item?.RemainingTime ?? TimeSpan.MaxValue).ThenBy(t => t.Added)
            .ToList();

    public TrackedDownload? Find(int queueId) => _tracked.Values.FirstOrDefault(t => t.QueueId == queueId);

    /// <summary>Episodes being downloaded (for the "already in queue" specification), with the queued release title.</summary>
    public IReadOnlyDictionary<int, string> QueuedEpisodes()
    {
        var queued = new Dictionary<int, string>();
        foreach (var download in _tracked.Values.Where(t => t.SeriesId is not null && t.State is not (TrackedDownloadState.Failed or TrackedDownloadState.FailedPending or TrackedDownloadState.Ignored or TrackedDownloadState.Imported)))
        {
            foreach (var episodeId in download.EpisodeIds)
            {
                queued.TryAdd(episodeId, download.Title);
            }
        }

        return queued;
    }

    public void TrackGrab(DownloadClient client, string downloadId, RemoteEpisode remote)
    {
        _tracked[(client.Id, downloadId)] = new TrackedDownload
        {
            DownloadClientId = client.Id,
            DownloadClientName = client.Name,
            DownloadId = downloadId,
            SeriesId = remote.Series?.Id ?? remote.Episodes.FirstOrDefault()?.SeriesId,
            EpisodeIds = remote.Episodes.Select(e => e.Id).ToList(),
            Title = remote.Release.Title,
            Quality = remote.Quality,
            Indexer = remote.Release.Indexer,
            Size = remote.Release.Size,
            Protocol = remote.Release.DownloadProtocol,
            Added = time.GetUtcNow().UtcDateTime,
        };
    }

    /// <summary>Polls every enabled client, updates the states and handles failed downloads.</summary>
    public async Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopes.CreateScope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<GenerralDbContext>();
            var history = services.GetRequiredService<HistoryService>();
            var completed = services.GetRequiredService<CompletedDownloadService>();
            var config = await services.GetRequiredService<DownloadClientConfigService>().GetAsync(cancellationToken);
            var clients = await db.DownloadClients.AsNoTracking().Where(c => c.Enable).ToListAsync(cancellationToken);
            var now = time.GetUtcNow().UtcDateTime;
            var redownload = new List<IReadOnlyList<int>>();
            var failed = 0;
            var imported = 0;

            foreach (var key in _tracked.Keys.Where(k => clients.All(c => c.Id != k.ClientId)))
            {
                _tracked.TryRemove(key, out _);
            }

            foreach (var client in clients)
            {
                if (provider.Find(client.Implementation) is not { } implementation)
                {
                    continue;
                }

                IReadOnlyList<DownloadClientItem> items;
                try
                {
                    items = await implementation.GetItemsAsync(client, cancellationToken);
                }
                catch (DownloadClientException e)
                {
                    logger.LogWarning("Unable to retrieve the queue of {Client}: {Message}", client.Name, e.Message);
                    continue;
                }

                var seen = new HashSet<string>();
                foreach (var item in items)
                {
                    seen.Add(item.DownloadId);
                    if (!_tracked.TryGetValue((client.Id, item.DownloadId), out var tracked))
                    {
                        tracked = await CreateAsync(history, client, item, cancellationToken);
                        _tracked[(client.Id, item.DownloadId)] = tracked;
                    }

                    tracked.Item = item;
                    tracked.DownloadClientName = client.Name;
                    var failure = Update(tracked, config, now);
                    if (failure is not null)
                    {
                        failed++;
                        if (await FailAsync(services, client, tracked, failure, FailedDownloadHandling, skipRedownload: false, automatic: true, config, cancellationToken) is { } ids)
                        {
                            redownload.Add(ids);
                        }
                    }
                    else if (tracked.State == TrackedDownloadState.ImportPending)
                    {
                        await completed.ImportAsync(client, tracked, cancellationToken);
                        imported += tracked.State == TrackedDownloadState.Imported ? 1 : 0;
                    }
                    else if (tracked.State == TrackedDownloadState.Imported)
                    {
                        await completed.RemoveIfSeededAsync(client, tracked, cancellationToken);
                    }
                }

                foreach (var (key, tracked) in _tracked.Where(p => p.Key.ClientId == client.Id && !seen.Contains(p.Key.DownloadId)))
                {
                    if (tracked.Item is not null || tracked.Added < now - UnseenGrace)
                    {
                        _tracked.TryRemove(key, out _);
                    }
                }
            }

            return new RefreshResult(_tracked.Count, failed, imported, redownload);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Queue → Remove: optionally removes the download from the client and marks it failed (blocklist).</summary>
    /// <returns>Episode ids to search again, or null.</returns>
    /// <exception cref="DownloadClientException">The client failed to remove the download.</exception>
    public async Task<IReadOnlyList<int>?> RemoveAsync(TrackedDownload tracked, bool removeFromClient, bool blocklist, bool skipRedownload, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<GenerralDbContext>();
        var client = await db.DownloadClients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == tracked.DownloadClientId, cancellationToken);
        if (removeFromClient && client is not null && provider.Find(client.Implementation) is { } implementation)
        {
            await implementation.RemoveItemAsync(client, tracked.DownloadId, deleteData: true, cancellationToken);
        }

        IReadOnlyList<int>? redownload = null;
        if (blocklist)
        {
            var config = await services.GetRequiredService<DownloadClientConfigService>().GetAsync(cancellationToken);
            redownload = await FailAsync(services, client, tracked, "Manually marked as failed", null, skipRedownload, automatic: false, config, cancellationToken);
        }
        else if (!removeFromClient)
        {
            var history = services.GetRequiredService<HistoryService>();
            if (await history.GrabbedAsync(tracked.DownloadId, cancellationToken) is { Count: > 0 } grabbed)
            {
                await history.RecordAsync(grabbed, EpisodeHistoryEventType.DownloadIgnored, "Manually ignored", null, cancellationToken);
            }
        }

        _tracked.TryRemove((tracked.DownloadClientId, tracked.DownloadId), out _);
        return redownload;
    }

    /// <returns>The failure message when the download must be handled as failed.</returns>
    private static string? Update(TrackedDownload tracked, DownloadClientConfig config, DateTime now)
    {
        var item = tracked.Item!;
        tracked.StalledSince = item.IsStalled ? tracked.StalledSince ?? now : null;
        if (tracked.State is TrackedDownloadState.Failed or TrackedDownloadState.Ignored or TrackedDownloadState.Imported)
        {
            return null;
        }

        tracked.Status = TrackedDownloadStatus.Ok;
        tracked.StatusMessages = [];
        var grabbed = tracked.SeriesId is not null;
        var stalledFor = now - tracked.StalledSince;

        if (item.Status == DownloadItemStatus.Failed)
        {
            if (grabbed)
            {
                tracked.State = TrackedDownloadState.FailedPending;
                return string.IsNullOrWhiteSpace(item.Message) ? "Failed download detected" : item.Message;
            }

            tracked.Warn("Download has failed but wasn't grabbed by Generral, skipping automatic download handling");
            return null;
        }

        if (grabbed && config.StalledDownloadTimeout > 0 && item.RemainingSize >= item.TotalSize && stalledFor >= TimeSpan.FromHours(config.StalledDownloadTimeout))
        {
            tracked.State = TrackedDownloadState.FailedPending;
            return $"Download stalled with no progress for more than {config.StalledDownloadTimeout} hour(s)";
        }

        if (item.Status == DownloadItemStatus.Completed)
        {
            tracked.State = grabbed ? TrackedDownloadState.ImportPending : TrackedDownloadState.ImportBlocked;
            if (!grabbed)
            {
                tracked.Warn("Download wasn't grabbed by Generral: manual import required");
            }

            return null;
        }

        tracked.State = TrackedDownloadState.Downloading;
        if (item.Status == DownloadItemStatus.Warning)
        {
            tracked.Warn(item.Message ?? "The download client reported a problem");
        }
        else if (stalledFor >= StalledWarningAfter)
        {
            tracked.Warn("The download is stalled with no connections");
        }

        return null;
    }

    /// <summary>Sonarr's <c>MarkAsFailed</c>: history <c>downloadFailed</c>, blocklist, optional removal, then the redownload decision.</summary>
    private async Task<IReadOnlyList<int>?> FailAsync(
        IServiceProvider services,
        DownloadClient? client,
        TrackedDownload tracked,
        string message,
        string? source,
        bool skipRedownload,
        bool automatic,
        DownloadClientConfig config,
        CancellationToken cancellationToken)
    {
        var history = services.GetRequiredService<HistoryService>();
        var grabbed = await history.GrabbedAsync(tracked.DownloadId, cancellationToken);
        if (grabbed.Count == 0)
        {
            tracked.Warn("Download wasn't grabbed by Generral, skipping failed download handling");
            return null;
        }

        tracked.State = TrackedDownloadState.Failed;
        tracked.Status = TrackedDownloadStatus.Error;
        tracked.StatusMessages = [new TrackedDownloadStatusMessage(tracked.Item?.Title ?? tracked.Title, [message])];
        await history.RecordAsync(grabbed, EpisodeHistoryEventType.DownloadFailed, message, source, cancellationToken);
        await services.GetRequiredService<BlocklistService>().BlockAsync(grabbed, message, source, cancellationToken);
        logger.LogWarning("Download {Title} failed: {Message}", tracked.Title, message);

        if (automatic && client is { RemoveFailedDownloads: true } && tracked.Item?.CanBeRemoved == true && provider.Find(client.Implementation) is { } implementation)
        {
            try
            {
                await implementation.RemoveItemAsync(client, tracked.DownloadId, deleteData: true, cancellationToken);
            }
            catch (DownloadClientException e)
            {
                logger.LogWarning("Couldn't remove failed download {Title} from {Client}: {Message}", tracked.Title, client.Name, e.Message);
            }
        }

        var releaseSource = Enum.TryParse<ReleaseSourceType>(grabbed[0].Data.GetValueOrDefault(EpisodeHistory.ReleaseSource), out var s) ? s : ReleaseSourceType.Unknown;
        if (skipRedownload || !config.AutoRedownloadFailed ||
            (releaseSource == ReleaseSourceType.InteractiveSearch && !config.AutoRedownloadFailedFromInteractiveSearch))
        {
            return null;
        }

        return grabbed.Select(h => h.EpisodeId).Distinct().ToList();
    }

    private static async Task<TrackedDownload> CreateAsync(HistoryService history, DownloadClient client, DownloadClientItem item, CancellationToken cancellationToken)
    {
        var tracked = new TrackedDownload
        {
            DownloadClientId = client.Id,
            DownloadClientName = client.Name,
            DownloadId = item.DownloadId,
            Title = item.Title,
        };

        var grabbed = await history.GrabbedAsync(item.DownloadId, cancellationToken);
        if (grabbed.Count == 0)
        {
            return tracked;
        }

        var grab = grabbed[0];
        tracked.SeriesId = grab.SeriesId;
        tracked.EpisodeIds = grabbed.Select(h => h.EpisodeId).Distinct().ToList();
        tracked.Title = grab.SourceTitle;
        tracked.Quality = grab.Quality;
        tracked.Indexer = grab.Data.GetValueOrDefault(EpisodeHistory.Indexer);
        tracked.Added = grab.Date;
        if (await history.HasEventAsync(item.DownloadId, EpisodeHistoryEventType.DownloadIgnored, cancellationToken))
        {
            tracked.State = TrackedDownloadState.Ignored;
        }
        else if (await history.ImportedEpisodesAsync(item.DownloadId, cancellationToken) is var imported && tracked.EpisodeIds.All(imported.Contains))
        {
            tracked.State = TrackedDownloadState.Imported;
        }
        else if (await history.HasEventAsync(item.DownloadId, EpisodeHistoryEventType.DownloadFailed, cancellationToken))
        {
            tracked.State = TrackedDownloadState.Failed;
            tracked.Status = TrackedDownloadStatus.Error;
            tracked.StatusMessages = [new TrackedDownloadStatusMessage(item.Title, ["Download failed"])];
        }

        return tracked;
    }
}

/// <summary><c>RefreshMonitoredDownloads</c> (task "Check For Finished Download"): refreshes the queue and searches again for failed grabs.</summary>
public sealed class RefreshMonitoredDownloadsHandler(TrackedDownloadService tracked) : ICommandHandler
{
    public const string CommandName = "RefreshMonitoredDownloads";

    public string Name => CommandName;

    public async Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken)
    {
        var result = await tracked.RefreshAsync(cancellationToken);
        foreach (var episodeIds in result.Redownload)
        {
            context.Enqueue(EpisodeSearchHandler.CommandName, new JsonObject { ["episodeIds"] = new JsonArray(episodeIds.Select(id => (JsonNode)id).ToArray()) });
        }

        return $"{result.Tracked} download(s) tracked, {result.Imported} imported, {result.Failed} failed";
    }
}
