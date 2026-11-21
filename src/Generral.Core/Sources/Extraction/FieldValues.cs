using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Generral.Core.Sources.Extraction;

/// <summary>
/// Field values flow through the pipeline as <c>string</c>, <c>long</c>, <c>double</c>, <c>bool</c>,
/// <see cref="DateTime"/> (UTC, or unspecified when the source gave no offset) or a JSON object/array.
/// </summary>
public static class FieldValues
{
    private const DateTimeStyles DateStyles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal;

    public static object? FromJson(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not JsonValue value)
        {
            return node.DeepClone();
        }

        var element = value.TryGetValue<JsonElement>(out var e) ? e : JsonSerializer.SerializeToElement(node);
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDouble(),
            _ => null,
        };
    }

    public static JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        long l => JsonValue.Create(l),
        int i => JsonValue.Create(i),
        double d => JsonValue.Create(d),
        bool b => JsonValue.Create(b),
        DateTime d => JsonValue.Create(FormatDate(d)),
        JsonNode node => node.DeepClone(),
        _ => JsonValue.Create(ToText(value)),
    };

    public static string? ToText(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        DateTime d => FormatDate(d),
        JsonValue v => ToText(FromJson(v)),
        JsonNode node => node.ToJsonString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    public static bool IsMissing(object? value) => value is null || (value is string s && string.IsNullOrWhiteSpace(s));

    /// <summary>Parses a date in any common format: values with an offset become UTC, others stay unspecified.</summary>
    public static bool TryParseDate(string text, string[]? formats, out DateTime result)
    {
        if (formats is null || formats.Any(IsIsoFormat))
        {
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateStyles, out result))
            {
                return true;
            }

            formats = formats?.Where(f => !IsIsoFormat(f)).ToArray();
            if (formats is null || formats.Length == 0)
            {
                return false;
            }
        }

        return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateStyles, out result);
    }

    /// <summary>Converts a date to UTC, interpreting unspecified dates in <paramref name="timezone"/>.</summary>
    public static DateTime ToUtc(DateTime value, TimeZoneInfo timezone) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => TimeZoneInfo.ConvertTimeToUtc(AdjustInvalid(value, timezone), timezone),
    };

    private static DateTime AdjustInvalid(DateTime value, TimeZoneInfo timezone)
    {
        // Wall-clock times skipped by a DST transition are moved forward by the transition gap.
        return timezone.IsInvalidTime(value) ? value.AddHours(1) : value;
    }

    private static bool IsIsoFormat(string format) =>
        format is "o" or "O" || format.Equals("iso", StringComparison.OrdinalIgnoreCase) || format.Equals("iso8601", StringComparison.OrdinalIgnoreCase);

    private static string FormatDate(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        : value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
}
