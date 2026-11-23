using System.Collections.Concurrent;

namespace Generral.Core.HealthCheck;

public enum ProviderKind
{
    Indexer,
    DownloadClient,
    Mapping,
    Source,
}

public sealed record ProviderFailure(ProviderKind Kind, int Id, string Name, string Message, DateTime Since);

/// <summary>
/// The last outcome of each indexer, download client, mapping and source, in memory (Generral's stand-in for Sonarr's provider
/// status tables; indexer back-off is in <see cref="Indexers.IndexerStatusService"/>). A change of outcome asks the scheduler for a health check.
/// </summary>
public sealed class ProviderStatusTracker(TimeProvider time)
{
    private readonly ConcurrentDictionary<(ProviderKind Kind, int Id), ProviderFailure> _failures = new();
    private int _changed;

    public void RecordSuccess(ProviderKind kind, int id)
    {
        if (_failures.TryRemove((kind, id), out _))
        {
            Interlocked.Exchange(ref _changed, 1);
        }
    }

    public void RecordFailure(ProviderKind kind, int id, string name, string message)
    {
        var added = false;
        _failures.AddOrUpdate(
            (kind, id),
            _ =>
            {
                added = true;
                return new ProviderFailure(kind, id, name, message, time.GetUtcNow().UtcDateTime);
            },
            (_, existing) => existing with { Name = name, Message = message });
        if (added)
        {
            Interlocked.Exchange(ref _changed, 1);
        }
    }

    public IReadOnlyList<ProviderFailure> Failures(ProviderKind kind) =>
        _failures.Values.Where(f => f.Kind == kind).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>True once after any outcome changed.</summary>
    public bool ConsumeChanged() => Interlocked.Exchange(ref _changed, 0) == 1;
}
