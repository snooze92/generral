using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Http;

namespace Generral.Core.Sources.Types;

/// <summary>Reads a source's settings object, collecting a validation failure per invalid property.</summary>
public sealed partial class SourceSettings(JsonObject settings)
{
    public const string RequestDelayField = "requestDelay";
    public const string HeadersField = "headers";
    public const string TimezoneField = "timezone";

    private static readonly TimeSpan MaxRequestDelay = TimeSpan.FromMinutes(1);

    private readonly List<SourceValidationFailure> _failures = [];

    public IReadOnlyList<SourceValidationFailure> Failures => _failures;

    /// <summary>The common fields (<c>requestDelay</c>, <c>headers</c>, <c>timezone</c>), after the type's own.</summary>
    public static IReadOnlyList<SourceField> CommonFields(TimeSpan defaultRequestDelay) =>
    [
        new()
        {
            Name = RequestDelayField,
            Label = "Request Delay",
            Type = SourceFieldType.Number,
            Unit = "seconds",
            Value = defaultRequestDelay.TotalSeconds,
            HelpText = "Wait between two requests of one refresh",
            Advanced = true,
        },
        new()
        {
            Name = HeadersField,
            Label = "Headers",
            Type = SourceFieldType.KeyValueList,
            Value = new JsonArray(),
            HelpText = "Extra request headers, e.g. for authentication",
            Advanced = true,
        },
        new()
        {
            Name = TimezoneField,
            Label = "Timezone",
            Value = "UTC",
            HelpText = "IANA timezone applied to dates without an offset, e.g. Europe/Paris",
            Advanced = true,
        },
    ];

    public void Fail(string property, string message) => _failures.Add(new SourceValidationFailure(property, message));

    public JsonNode? this[string name] => settings[name];

    public string? String(string name, bool required = false)
    {
        var text = settings[name] switch
        {
            null => null,
            JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
            JsonValue value when value.GetValueKind() == JsonValueKind.Number => FieldValues.ToText(FieldValues.FromJson(value)),
            _ => Invalid<string>(name, "must be a string"),
        };

        text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (text is null && required)
        {
            Fail(name, "is required");
        }

        return text;
    }

    public bool Bool(string name, bool defaultValue) => settings[name] switch
    {
        null => defaultValue,
        JsonValue value when value.GetValueKind() is JsonValueKind.True or JsonValueKind.False => value.GetValue<bool>(),
        _ => Invalid(name, "must be true or false", defaultValue),
    };

    public TimeSpan RequestDelay(TimeSpan defaultValue)
    {
        var delay = settings[RequestDelayField] switch
        {
            null => defaultValue,
            JsonValue value when FieldValues.FromJson(value) is long seconds => TimeSpan.FromSeconds(seconds),
            JsonValue value when FieldValues.FromJson(value) is double seconds && double.IsFinite(seconds) => TimeSpan.FromSeconds(seconds),
            JsonValue value when FieldValues.FromJson(value) is string text && TryParseDuration(text, out var parsed) => parsed,
            _ => Invalid(RequestDelayField, "must be a number of seconds or a duration like '500ms' or '2s'", defaultValue),
        };

        if (delay < TimeSpan.Zero || delay > MaxRequestDelay)
        {
            Fail(RequestDelayField, $"must be between 0 and {MaxRequestDelay.TotalSeconds:0} seconds");
            return defaultValue;
        }

        return delay;
    }

    /// <summary>Accepts a <c>{ name: value }</c> object or Sonarr's key/value list (<c>[{ key, value }]</c>).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers()
    {
        var pairs = settings[HeadersField] switch
        {
            null => [],
            JsonObject obj => obj.Select(p => KeyValuePair.Create(p.Key, FieldValues.ToText(FieldValues.FromJson(p.Value)) ?? "")).ToList(),
            JsonArray array when array.All(i => i is JsonObject) => array.Select(i => KeyValuePair.Create(
                FieldValues.ToText(FieldValues.FromJson(i!["key"])) ?? "",
                FieldValues.ToText(FieldValues.FromJson(i["value"])) ?? "")).ToList(),
            _ => Invalid<List<KeyValuePair<string, string>>>(HeadersField, "must be an object of header names to values") ?? [],
        };

        foreach (var (name, _) in pairs)
        {
            if (!HeaderName().IsMatch(name))
            {
                Fail(HeadersField, $"'{name}' is not a valid header name");
            }
        }

        return pairs.Where(p => HeaderName().IsMatch(p.Key)).ToList();
    }

    public TimeZoneInfo Timezone()
    {
        var id = String(TimezoneField);
        if (id is null)
        {
            return TimeZoneInfo.Utc;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var timezone))
        {
            return timezone;
        }

        Fail(TimezoneField, $"unknown timezone '{id}'");
        return TimeZoneInfo.Utc;
    }

    public SourceRequestSettings RequestSettings(TimeSpan defaultRequestDelay, SourceFetchOptions? options) => new()
    {
        RequestDelay = RequestDelay(defaultRequestDelay),
        Headers = Headers(),
        BypassCache = options?.BypassCache ?? false,
    };

    /// <exception cref="SourceFetchException">There are validation failures.</exception>
    public void ThrowIfInvalid()
    {
        if (_failures.Count > 0)
        {
            throw new SourceFetchException("Invalid settings: " + string.Join("; ", _failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}")));
        }
    }

    private static bool TryParseDuration(string text, out TimeSpan duration)
    {
        var match = Duration().Match(text.Trim());
        duration = default;
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            return false;
        }

        duration = match.Groups[2].Value switch
        {
            "ms" => TimeSpan.FromMilliseconds(amount),
            "m" => TimeSpan.FromMinutes(amount),
            _ => TimeSpan.FromSeconds(amount),
        };
        return true;
    }

    private T? Invalid<T>(string name, string message, T? fallback = default)
    {
        Fail(name, message);
        return fallback;
    }

    [GeneratedRegex(@"^([0-9]+(?:\.[0-9]+)?)\s*(ms|s|m)?$")]
    private static partial Regex Duration();

    [GeneratedRegex(@"^[!#$%&'*+.^_`|~0-9A-Za-z-]+$")]
    private static partial Regex HeaderName();
}
