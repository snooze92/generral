using System.Collections.Concurrent;
using System.Net;

namespace Generral.Core.Sources.Http;

/// <summary>Raw responses of successful source requests, per URL and headers, so that previews don't refetch.</summary>
public sealed class SourceResponseCache(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    public const int MaxEntries = 256;

    private readonly ConcurrentDictionary<string, CachedResponse> _entries = new();

    public bool TryGet(string key, out CachedResponse response)
    {
        if (_entries.TryGetValue(key, out response!) && response.Expires > time.GetUtcNow())
        {
            return true;
        }

        _entries.TryRemove(key, out _);
        return false;
    }

    public void Set(string key, CachedResponse response)
    {
        if (_entries.Count >= MaxEntries)
        {
            var now = time.GetUtcNow();
            foreach (var entry in _entries.Where(e => e.Value.Expires <= now).ToList())
            {
                _entries.TryRemove(entry);
            }

            while (_entries.Count >= MaxEntries && _entries.MinBy(e => e.Value.Expires) is { Key: not null } oldest)
            {
                _entries.TryRemove(oldest);
            }
        }

        _entries[key] = response;
    }

    public CachedResponse Create(HttpStatusCode status, string? contentType, string content) =>
        new(status, contentType, content, time.GetUtcNow() + Lifetime);

    public static string Key(Uri url, IEnumerable<KeyValuePair<string, string>> headers) =>
        string.Join('\n', headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase).Select(h => $"{h.Key.ToLowerInvariant()}:{h.Value}").Prepend(url.AbsoluteUri));
}

public sealed record CachedResponse(HttpStatusCode Status, string? ContentType, string Content, DateTimeOffset Expires);
