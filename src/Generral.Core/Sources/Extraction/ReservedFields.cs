using System.Text.Json.Nodes;
using Generral.Core.Datastore;

namespace Generral.Core.Sources.Extraction;

/// <summary>Maps the reserved fields (<c>key</c>, <c>title</c>, <c>start</c>, …) onto an event's core attributes.</summary>
public static class ReservedFields
{
    public const string Key = "key";
    public const string Title = "title";
    public const string Start = "start";
    public const string End = "end";
    public const string Status = "status";
    public const string Raw = "_raw";

    private static readonly Dictionary<string, EventStatus> Statuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["scheduled"] = EventStatus.Scheduled,
        ["upcoming"] = EventStatus.Scheduled,
        ["confirmed"] = EventStatus.Scheduled,
        ["not started"] = EventStatus.Scheduled,
        ["ns"] = EventStatus.Scheduled,
        ["tbd"] = EventStatus.Scheduled,
        ["time to be defined"] = EventStatus.Scheduled,
        ["live"] = EventStatus.Scheduled,
        ["in progress"] = EventStatus.Scheduled,
        ["postponed"] = EventStatus.Postponed,
        ["pst"] = EventStatus.Postponed,
        ["delayed"] = EventStatus.Postponed,
        ["rescheduled"] = EventStatus.Postponed,
        ["cancelled"] = EventStatus.Cancelled,
        ["canceled"] = EventStatus.Cancelled,
        ["canc"] = EventStatus.Cancelled,
        ["abandoned"] = EventStatus.Cancelled,
        ["abd"] = EventStatus.Cancelled,
        ["completed"] = EventStatus.Completed,
        ["complete"] = EventStatus.Completed,
        ["finished"] = EventStatus.Completed,
        ["match finished"] = EventStatus.Completed,
        ["final"] = EventStatus.Completed,
        ["ft"] = EventStatus.Completed,
        ["aet"] = EventStatus.Completed,
        ["pen"] = EventStatus.Completed,
        ["ended"] = EventStatus.Completed,
        ["done"] = EventStatus.Completed,
    };

    public static IReadOnlySet<string> ImageFields { get; } = new HashSet<string> { "poster", "fanart", "thumb", "banner", "logo" };

    public static EventStatus ParseStatus(string? value)
    {
        var normalised = string.Join(' ', (value ?? "").Replace('_', ' ').Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return Statuses.GetValueOrDefault(normalised, EventStatus.Unknown);
    }

    /// <summary>Builds the event, or returns null after adding the reasons to <paramref name="errors"/>.</summary>
    /// <param name="baseUrl">Relative image URLs are resolved against it.</param>
    public static ExtractedEvent? ToEvent(IReadOnlyDictionary<string, object?> values, JsonObject fields, TimeZoneInfo timezone, Uri? baseUrl, ICollection<string> errors)
    {
        var errorCount = errors.Count;
        var key = RequiredText(values, Key, errors);
        var title = RequiredText(values, Title, errors);
        var start = Date(values, Start, timezone, errors);
        var end = Date(values, End, timezone, errors);
        if (errors.Count > errorCount)
        {
            return null;
        }

        return new ExtractedEvent
        {
            Key = key!,
            Title = title!,
            Start = start,
            End = end,
            Timezone = timezone == TimeZoneInfo.Utc ? null : timezone.Id,
            Status = values.TryGetValue(Status, out var status) ? ParseStatus(FieldValues.ToText(status)) : EventStatus.Unknown,
            Fields = fields,
            Images = new ImageSet
            {
                Poster = Image(values, "poster", baseUrl),
                Fanart = Image(values, "fanart", baseUrl),
                Thumb = Image(values, "thumb", baseUrl),
                Banner = Image(values, "banner", baseUrl),
                Logo = Image(values, "logo", baseUrl),
            },
        };
    }

    private static string? RequiredText(IReadOnlyDictionary<string, object?> values, string name, ICollection<string> errors)
    {
        var text = FieldValues.ToText(values.GetValueOrDefault(name))?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors.Add($"{name}: required");
            return null;
        }

        return text;
    }

    private static DateTime? Date(IReadOnlyDictionary<string, object?> values, string name, TimeZoneInfo timezone, ICollection<string> errors)
    {
        switch (values.GetValueOrDefault(name))
        {
            case null:
                return null;
            case DateTime date:
                return FieldValues.ToUtc(date, timezone);
            case long seconds when Math.Abs(seconds) < 100_000_000_000:
                return DateTime.UnixEpoch.AddSeconds(seconds);
            case long milliseconds:
                return DateTime.UnixEpoch.AddMilliseconds(milliseconds);
            case string text when string.IsNullOrWhiteSpace(text):
                return null;
            case string text when FieldValues.TryParseDate(text.Trim(), null, out var parsed):
                return FieldValues.ToUtc(parsed, timezone);
            case var other:
                errors.Add($"{name}: '{FieldValues.ToText(other)}' is not a date (use the dateparse filter)");
                return null;
        }
    }

    private static string? Image(IReadOnlyDictionary<string, object?> values, string name, Uri? baseUrl)
    {
        var text = FieldValues.ToText(values.GetValueOrDefault(name))?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var resolved = baseUrl is null ? Uri.TryCreate(text, UriKind.Absolute, out var absolute) ? absolute : null
            : Uri.TryCreate(baseUrl, text, out var relative) ? relative : null;
        return resolved is { Scheme: "http" or "https" } ? resolved.AbsoluteUri : null;
    }
}
