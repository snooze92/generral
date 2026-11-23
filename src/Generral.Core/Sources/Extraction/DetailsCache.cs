using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Generral.Core.Sources.Extraction;

/// <summary>
/// Extracted details fields per item (see <see cref="CompiledDetails.CacheKey"/>), so that refreshes only request the details of
/// new or changed items. In memory: a restart fetches every item's details once.
/// </summary>
public sealed class DetailsCache(TimeProvider time)
{
    public const int MaxEntries = 10_000;

    private readonly ConcurrentDictionary<string, (JsonObject Fields, DateTimeOffset Expires)> _entries = new();

    public JsonObject? TryGet(string key)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.Expires > time.GetUtcNow())
        {
            return (JsonObject)entry.Fields.DeepClone();
        }

        _entries.TryRemove(key, out _);
        return null;
    }

    public void Set(string key, JsonObject fields, TimeSpan lifetime)
    {
        if (_entries.Count >= MaxEntries)
        {
            foreach (var oldest in _entries.OrderBy(e => e.Value.Expires).Take(MaxEntries / 10).ToList())
            {
                _entries.TryRemove(oldest.Key, out _);
            }
        }

        _entries[key] = ((JsonObject)fields.DeepClone(), time.GetUtcNow() + lifetime);
    }
}
