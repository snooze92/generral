using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Generral.Core.Extensions;

/// <summary>Typed reads of provider settings (Sonarr field values) stored as JSON.</summary>
public static class SettingsJson
{
    public static string? Text(this JsonObject settings, string name) =>
        settings[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>().Trim() is { Length: > 0 } s ? s : null;

    public static bool Bool(this JsonObject settings, string name, bool fallback = false) => settings[name] switch
    {
        JsonValue v when v.GetValueKind() == JsonValueKind.True => true,
        JsonValue v when v.GetValueKind() == JsonValueKind.False => false,
        _ => fallback,
    };

    public static int? Int(this JsonObject settings, string name) => settings[name] switch
    {
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => (int)v.Deserialize<double>(),
        JsonValue v when v.GetValueKind() == JsonValueKind.String && int.TryParse(v.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        _ => null,
    };
}
