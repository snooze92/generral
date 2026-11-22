using System.Text.Json;
using Generral.Core.Datastore;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Configuration;

/// <summary>Settings → Indexers (Sonarr's <c>IndexerConfigResource</c> subset).</summary>
public sealed record IndexerConfig
{
    public const int MinimumRssSyncInterval = 10;

    public const int MaximumRssSyncInterval = 120;

    /// <summary>Minutes, 0 disables RSS sync.</summary>
    public int RssSyncInterval { get; init; } = 15;

    /// <returns>An error message, or null.</returns>
    public string? Validate() =>
        RssSyncInterval is 0 or (>= MinimumRssSyncInterval and <= MaximumRssSyncInterval)
            ? null
            : $"RSS sync interval must be 0 (disabled) or between {MinimumRssSyncInterval} and {MaximumRssSyncInterval} minutes";
}

public sealed class IndexerConfigService(GenerralDbContext db)
{
    private const string Key = "indexer";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IndexerConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        var entry = await db.Config.AsNoTracking().FirstOrDefaultAsync(c => c.Key == Key, cancellationToken);
        return entry is null ? new IndexerConfig() : JsonSerializer.Deserialize<IndexerConfig>(entry.Value, JsonOptions) ?? new IndexerConfig();
    }

    /// <exception cref="ArgumentException">Invalid settings.</exception>
    public async Task<IndexerConfig> SaveAsync(IndexerConfig config)
    {
        if (config.Validate() is { } error)
        {
            throw new ArgumentException(error);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var entry = await db.Config.FirstOrDefaultAsync(c => c.Key == Key);
        if (entry is null)
        {
            db.Config.Add(new ConfigEntry { Key = Key, Value = json });
        }
        else
        {
            entry.Value = json;
        }

        await db.SaveChangesAsync();
        return config;
    }
}
