using System.Text.Json;
using Generral.Core.Datastore;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Configuration;

/// <summary>Settings → UI. Field names and defaults match Sonarr.</summary>
public sealed record UiSettings
{
    public int FirstDayOfWeek { get; init; }

    public string CalendarWeekColumnHeader { get; init; } = "ddd M/D";

    public string ShortDateFormat { get; init; } = "MMM D YYYY";

    public string LongDateFormat { get; init; } = "dddd, MMMM D YYYY";

    public string TimeFormat { get; init; } = "h(:mm)a";

    public string TimeZone { get; init; } = string.Empty;

    public bool ShowRelativeDates { get; init; } = true;

    public bool EnableColorImpairedMode { get; init; }

    public string HeaderColor { get; init; } = "fullColor";

    public string Theme { get; init; } = "auto";

    /// <summary>Generral-specific: hide results in titles/overviews by default (see spec 05 § Spoilers).</summary>
    public bool SpoilerFreeMode { get; init; } = true;
}

public interface IUiSettingsService
{
    UiSettings Get();

    UiSettings Save(UiSettings settings);
}

public class UiSettingsService(GenerralDbContext db) : IUiSettingsService
{
    private const string Key = "ui";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public UiSettings Get()
    {
        var entry = db.Config.AsNoTracking().FirstOrDefault(c => c.Key == Key);

        return entry == null ? new UiSettings() : JsonSerializer.Deserialize<UiSettings>(entry.Value, JsonOptions) ?? new UiSettings();
    }

    public UiSettings Save(UiSettings settings)
    {
        Validate(settings);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var entry = db.Config.FirstOrDefault(c => c.Key == Key);

        if (entry == null)
        {
            db.Config.Add(new ConfigEntry { Key = Key, Value = json });
        }
        else
        {
            entry.Value = json;
        }

        db.SaveChanges();

        return settings;
    }

    private static void Validate(UiSettings settings)
    {
        if (settings.FirstDayOfWeek is < 0 or > 1)
        {
            throw new ArgumentException("First day of week must be 0 (Sunday) or 1 (Monday)");
        }

        if (settings.Theme is not ("auto" or "light" or "dark"))
        {
            throw new ArgumentException("Theme must be auto, light or dark");
        }

        if (settings.HeaderColor is not ("fullColor" or "gradient"))
        {
            throw new ArgumentException("Header color must be fullColor or gradient");
        }
    }
}
