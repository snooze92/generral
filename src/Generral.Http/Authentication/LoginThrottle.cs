using System.Collections.Concurrent;

namespace Generral.Http.Authentication;

/// <summary>Locks out a client IP after repeated failed sign-ins.</summary>
public class LoginThrottle(TimeProvider time)
{
    internal const int MaxFailures = 5;
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan Lockout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public DateTimeOffset? GetLockoutEnd(string client)
    {
        if (_entries.TryGetValue(client, out var entry) && entry.LockedUntil is { } until && until > time.GetUtcNow())
        {
            return until;
        }

        return null;
    }

    public void RecordFailure(string client)
    {
        var now = time.GetUtcNow();

        _entries.AddOrUpdate(
            client,
            _ => new Entry(now, 1, null),
            (_, entry) =>
            {
                var count = now - entry.FirstFailure > Window ? 1 : entry.Count + 1;
                var first = count == 1 ? now : entry.FirstFailure;

                return new Entry(first, count, count >= MaxFailures ? now + Lockout : null);
            });

        Prune(now);
    }

    public void RecordSuccess(string client)
    {
        _entries.TryRemove(client, out _);
    }

    private void Prune(DateTimeOffset now)
    {
        if (_entries.Count < 1000)
        {
            return;
        }

        foreach (var (key, entry) in _entries)
        {
            if (now - entry.FirstFailure > Window && (entry.LockedUntil == null || entry.LockedUntil < now))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    private sealed record Entry(DateTimeOffset FirstFailure, int Count, DateTimeOffset? LockedUntil);
}
