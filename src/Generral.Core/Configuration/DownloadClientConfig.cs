using System.Text.Json;
using Generral.Core.Datastore;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Configuration;

/// <summary>Settings → Download Clients (Sonarr's <c>DownloadClientConfigResource</c> subset, plus stalled handling).</summary>
public sealed record DownloadClientConfig
{
    public const int MaximumStalledDownloadTimeout = 168;

    /// <summary>Search again after a failed download.</summary>
    public bool AutoRedownloadFailed { get; init; } = true;

    public bool AutoRedownloadFailedFromInteractiveSearch { get; init; } = true;

    /// <summary>Hours a torrent may stay stalled at 0 % before it is treated as failed; 0 disables stalled handling.</summary>
    public int StalledDownloadTimeout { get; init; }

    /// <returns>An error message, or null.</returns>
    public string? Validate() =>
        StalledDownloadTimeout is >= 0 and <= MaximumStalledDownloadTimeout
            ? null
            : $"Stalled download timeout must be between 0 (disabled) and {MaximumStalledDownloadTimeout} hours";
}

public sealed class DownloadClientConfigService(GenerralDbContext db)
{
    private const string Key = "downloadclient";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DownloadClientConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        var entry = await db.Config.AsNoTracking().FirstOrDefaultAsync(c => c.Key == Key, cancellationToken);
        return entry is null ? new DownloadClientConfig() : JsonSerializer.Deserialize<DownloadClientConfig>(entry.Value, JsonOptions) ?? new DownloadClientConfig();
    }

    /// <exception cref="ArgumentException">Invalid settings.</exception>
    public async Task<DownloadClientConfig> SaveAsync(DownloadClientConfig config)
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
