using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Http;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace Generral.Core.Sources.Types;

/// <summary>
/// An ICS/webcal feed: one event per <c>VEVENT</c> (recurrences are not expanded). Fields: <c>uid</c>, <c>summary</c>,
/// <c>location</c>, <c>description</c>, <c>categories</c>, <c>url</c>, <c>status</c>, plus the reserved fields.
/// </summary>
public sealed class IcalSourceType(SourceFetcherFactory fetchers) : SourceType<Uri?>(fetchers)
{
    private const int RawLimit = 16 * 1024;

    public override string Id => "ical";

    public override string Name => "iCalendar";

    protected override IReadOnlyList<SourceField> TypeFields { get; } =
    [
        new()
        {
            Name = "url",
            Label = "URL",
            Placeholder = "https://example.org/calendar.ics",
            HelpText = "ICS feed URL (webcal:// is fetched over https)",
        },
    ];

    protected override Uri? Read(SourceSettings settings)
    {
        var text = settings.String("url", required: true);
        if (text is null)
        {
            return null;
        }

        if (text.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://" + text["webcal://".Length..];
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
        {
            return url;
        }

        settings.Fail("url", "must be an http(s) or webcal URL");
        return null;
    }

    protected override async Task<SourceFetchResult> FetchAsync(Uri? config, SourceFetchContext context, CancellationToken cancellationToken)
    {
        var document = await context.Fetcher.FetchAsync(config!, cancellationToken);
        Calendar calendar;
        try
        {
            calendar = Calendar.Load(document.Content) ?? throw new FormatException("no VCALENDAR");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new SourceFetchException($"{document.Url}: invalid iCalendar ({e.Message})", innerException: e);
        }

        // Ical.Net generates a random UID when one is missing, which would change the key on every refresh.
        var withUid = VeventsWithUid(document.Content);
        var hasUid = withUid.Count == calendar.Events.Count ? withUid : calendar.Events.Select(_ => true).ToList();
        var keys = new HashSet<string>();
        var items = calendar.Events.Select((e, i) => ToItem(e, hasUid[i], i, document.Url, context.Timezone, keys)).ToList();
        var meta = new JsonObject();
        if (calendar.Properties["X-WR-CALNAME"]?.Value is string name)
        {
            meta["name"] = name;
        }

        return Result(context, items, meta);
    }

    private static ExtractedItem ToItem(CalendarEvent vevent, bool hasUid, int index, Uri url, TimeZoneInfo timezone, HashSet<string> keys)
    {
        var errors = new List<string>();
        var uid = hasUid ? vevent.Uid?.Trim() : null;
        if (string.IsNullOrEmpty(uid))
        {
            errors.Add("key: required (UID)");
        }
        else if (vevent.RecurrenceIdentifier?.StartTime is { } recurrence)
        {
            uid = $"{uid}/{Utc(recurrence, timezone):yyyyMMdd'T'HHmmss'Z'}";
        }

        var title = vevent.Summary?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            errors.Add("title: required (SUMMARY)");
        }

        DateTime? start = vevent.DtStart is { } dtStart ? Utc(dtStart, timezone) : null;
        DateTime? end = vevent.DtEnd is { } dtEnd ? Utc(dtEnd, timezone)
            : vevent.DtStart is { } from && vevent.Duration is { } duration ? Utc(from.Add(duration), timezone)
            : null;
        var eventUrl = vevent.Url is { IsAbsoluteUri: true, Scheme: "http" or "https" } link ? link.AbsoluteUri : null;

        var fields = new JsonObject
        {
            [ReservedFields.Key] = uid,
            [ReservedFields.Title] = title,
            [ReservedFields.Start] = FieldValues.ToJson(start),
            [ReservedFields.End] = FieldValues.ToJson(end),
            [ReservedFields.Status] = vevent.Status,
            ["uid"] = hasUid ? vevent.Uid : null,
            ["summary"] = vevent.Summary,
            ["location"] = vevent.Location,
            ["description"] = vevent.Description,
            ["categories"] = new JsonArray(vevent.Categories.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
            ["url"] = eventUrl,
            ["allDay"] = vevent.IsAllDay,
        };
        fields[ReservedFields.Raw] = Raw(vevent, hasUid);

        if (errors.Count == 0 && !keys.Add(uid!))
        {
            errors.Add($"key: duplicate key '{uid}'");
        }

        var extracted = errors.Count > 0 ? null : new ExtractedEvent
        {
            Key = uid!,
            Title = title!,
            Start = start,
            End = end,
            Timezone = timezone == TimeZoneInfo.Utc ? null : timezone.Id,
            Status = ReservedFields.ParseStatus(vevent.Status),
            Fields = fields,
            Images = new ImageSet(),
        };
        return new ExtractedItem(index, url, fields, extracted, errors);
    }

    /// <summary>The event's properties as text (name → value, or values when repeated), up to <see cref="RawLimit"/> characters per value.</summary>
    private static JsonObject Raw(CalendarEvent vevent, bool hasUid)
    {
        var raw = new JsonObject();
        foreach (var group in vevent.Properties.Where(p => hasUid || p.Name != "UID").GroupBy(p => p.Name))
        {
            var values = group.Select(p => Text(p.Value)).ToArray();
            raw[group.Key] = values.Length == 1 ? values[0] : new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
        }

        return raw;
    }

    private static string? Text(object? value)
    {
        var text = value switch
        {
            null => null,
            CalDateTime date => date.ToString(),
            IEnumerable<string> list => string.Join(',', list),
            _ => value.ToString(),
        };
        return text is { Length: > RawLimit } ? text[..RawLimit] : text;
    }

    /// <summary>For each top-level VEVENT of the document, in order, whether it has a UID property.</summary>
    private static List<bool> VeventsWithUid(string content)
    {
        var result = new List<bool>();
        var depth = 0;
        foreach (var line in content.Split('\n'))
        {
            var text = line.TrimEnd('\r');
            if (text.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase))
            {
                depth++;
                if (depth == 2 && text.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(false);
                }
            }
            else if (text.StartsWith("END:", StringComparison.OrdinalIgnoreCase))
            {
                depth--;
            }
            else if (depth == 2 && result.Count > 0 && (text.StartsWith("UID:", StringComparison.OrdinalIgnoreCase) || text.StartsWith("UID;", StringComparison.OrdinalIgnoreCase)))
            {
                result[^1] = true;
            }
        }

        return result;
    }

    /// <summary>Floating times and all-day dates are interpreted in the source timezone.</summary>
    private static DateTime Utc(CalDateTime value, TimeZoneInfo timezone) =>
        value.IsFloating ? FieldValues.ToUtc(DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified), timezone) : value.AsUtc;
}
