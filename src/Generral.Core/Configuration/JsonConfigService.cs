using System.Text.Json;
using Generral.Core.Datastore;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Configuration;

/// <summary>A settings record stored as JSON under one <see cref="ConfigEntry"/> key.</summary>
public abstract class JsonConfigService<T>(GenerralDbContext db, string key)
    where T : class, new()
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> GetAsync(CancellationToken cancellationToken = default)
    {
        var entry = await db.Config.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key, cancellationToken);
        return entry is null ? new T() : JsonSerializer.Deserialize<T>(entry.Value, JsonOptions) ?? new T();
    }

    /// <exception cref="ArgumentException">Invalid settings.</exception>
    public async Task<T> SaveAsync(T config, CancellationToken cancellationToken = default)
    {
        if (Validate(config) is { } error)
        {
            throw new ArgumentException(error);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var entry = await db.Config.FirstOrDefaultAsync(c => c.Key == key, cancellationToken);
        if (entry is null)
        {
            db.Config.Add(new ConfigEntry { Key = key, Value = json });
        }
        else
        {
            entry.Value = json;
        }

        await db.SaveChangesAsync(cancellationToken);
        return config;
    }

    /// <returns>An error message, or null.</returns>
    protected virtual string? Validate(T config) => null;
}
