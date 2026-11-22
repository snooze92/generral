using System.Collections.Concurrent;
using Generral.Core.DecisionEngine;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Download;

/// <summary>Sends a release to a download client.</summary>
public interface IDownloadService
{
    Task GrabAsync(RemoteEpisode remoteEpisode, CancellationToken cancellationToken = default);
}

public sealed record PendingGrab(string Title, string Guid, int IndexerId, IReadOnlyList<int> EpisodeIds, DateTime Grabbed);

/// <summary>Until download clients exist: remembers the grab (in memory, most recent first) and logs that there is no client.</summary>
public sealed class PendingGrabDownloadService(TimeProvider time, ILogger<PendingGrabDownloadService> logger) : IDownloadService
{
    public const int Retained = 100;

    private readonly ConcurrentQueue<PendingGrab> _grabs = new();

    public IReadOnlyList<PendingGrab> Grabs => _grabs.Reverse().ToList();

    public Task GrabAsync(RemoteEpisode remoteEpisode, CancellationToken cancellationToken = default)
    {
        var release = remoteEpisode.Release;
        var episodes = remoteEpisode.Episodes.Select(e => e.Id).ToList();
        _grabs.Enqueue(new PendingGrab(release.Title, release.Guid, release.IndexerId, episodes, time.GetUtcNow().UtcDateTime));
        while (_grabs.Count > Retained && _grabs.TryDequeue(out _))
        {
        }

        logger.LogWarning("No download client: not sending {Title} from {Indexer} for {Episodes}", release.Title, release.Indexer,
            string.Join(", ", remoteEpisode.Episodes.Select(SearchCandidate.Format)));
        return Task.CompletedTask;
    }
}
