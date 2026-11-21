using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Generral.Core.Extensions;
using Json.Path;

namespace Generral.Core.Sources.Extraction;

/// <summary>A compiled filter. Throws <see cref="FieldException"/> when the value cannot be processed.</summary>
public delegate object? FieldFilter(object? value);

/// <summary>Cardigann-style field filters (see docs/specs/04-sources.md § Filters), implemented from their documented semantics.</summary>
public static class FieldFilters
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Dictionary<string, Func<IReadOnlyList<string>, TimeProvider, FieldFilter>> Factories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["replace"] = (args, _) => Replace(args),
        ["re_replace"] = (args, _) => RegexReplace(args),
        ["regexp"] = (args, _) => RegexCapture(args),
        ["split"] = (args, _) => Split(args),
        ["trim"] = (args, _) => Trim(args),
        ["prepend"] = (args, _) => Prepend(args),
        ["append"] = (args, _) => Append(args),
        ["tolower"] = (args, _) => Text(args, 0, s => s.ToLowerInvariant()),
        ["toupper"] = (args, _) => Text(args, 0, s => s.ToUpperInvariant()),
        ["urldecode"] = (args, _) => Text(args, 0, WebUtility.UrlDecode),
        ["urlencode"] = (args, _) => Text(args, 0, WebUtility.UrlEncode),
        ["htmldecode"] = (args, _) => Text(args, 0, WebUtility.HtmlDecode),
        ["diacritics"] = (args, _) => Text(args, 1, s => s.RemoveDiacritics()),
        ["slugify"] = (args, _) => Text(args, 0, s => s.Slugify()),
        ["dateparse"] = (args, _) => DateParse(args),
        ["timeago"] = (args, time) => TimeAgo(args, time, fuzzy: false),
        ["fuzzytime"] = (args, time) => TimeAgo(args, time, fuzzy: true),
        ["timezone"] = (args, _) => Timezone(args),
        ["querystring"] = (args, _) => QueryString(args),
        ["validate"] = (args, _) => Validate(args),
        ["jsonjoinarray"] = (args, _) => JsonJoinArray(args),
        ["toint"] = (args, _) => ToInt(args),
        ["tofloat"] = (args, _) => ToFloat(args),
    };

    public static IReadOnlyCollection<string> Names => Factories.Keys;

    /// <exception cref="ArgumentException">Unknown filter or invalid arguments.</exception>
    public static FieldFilter Create(string name, IReadOnlyList<string> args, TimeProvider? time = null)
    {
        if (!Factories.TryGetValue(name, out var factory))
        {
            throw new ArgumentException($"Unknown filter '{name}'");
        }

        return factory(args, time ?? TimeProvider.System);
    }

    private static FieldFilter Replace(IReadOnlyList<string> args)
    {
        Expect(args, 2);
        var (from, to) = (args[0], args[1]);
        if (from.Length == 0)
        {
            throw new ArgumentException("replace: the search string must not be empty");
        }

        return value => Map(value, s => s.Replace(from, to, StringComparison.Ordinal));
    }

    private static FieldFilter RegexReplace(IReadOnlyList<string> args)
    {
        Expect(args, 2);
        var regex = CreateRegex(args[0]);
        var replacement = args[1];
        return value => Map(value, s => regex.Replace(s, replacement));
    }

    private static FieldFilter RegexCapture(IReadOnlyList<string> args)
    {
        Expect(args, 1);
        var regex = CreateRegex(args[0]);
        return value => MapNullable(value, s =>
        {
            var match = regex.Match(s);
            if (!match.Success)
            {
                return null;
            }

            return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
        });
    }

    private static FieldFilter Split(IReadOnlyList<string> args)
    {
        Expect(args, 2);
        var separator = args[0];
        if (separator.Length == 0)
        {
            throw new ArgumentException("split: the separator must not be empty");
        }

        var index = ParseIntArg(args[1], "split");
        return value => MapNullable(value, s =>
        {
            var parts = s.Split(separator);
            var i = index < 0 ? parts.Length + index : index;
            return i >= 0 && i < parts.Length ? parts[i] : null;
        });
    }

    private static FieldFilter Trim(IReadOnlyList<string> args)
    {
        var chars = ArgsUpTo(args, 1).FirstOrDefault()?.ToCharArray();
        return value => Map(value, s => chars is { Length: > 0 } ? s.Trim(chars) : s.Trim());
    }

    private static FieldFilter Prepend(IReadOnlyList<string> args)
    {
        Expect(args, 1);
        return value => Map(value, s => args[0] + s);
    }

    private static FieldFilter Append(IReadOnlyList<string> args)
    {
        Expect(args, 1);
        return value => Map(value, s => s + args[0]);
    }

    private static FieldFilter Text(IReadOnlyList<string> args, int maxArgs, Func<string, string> transform)
    {
        if (args.Count > maxArgs)
        {
            throw new ArgumentException($"expected at most {maxArgs} argument(s), got {args.Count}");
        }

        return value => Map(value, transform);
    }

    private static FieldFilter DateParse(IReadOnlyList<string> args)
    {
        var formats = args.Count == 0 ? null : args.ToArray();
        return value => value switch
        {
            null => null,
            DateTime d => d,
            _ => FieldValues.TryParseDate(AsText(value).Trim(), formats, out var result)
                ? result
                : throw new FieldException($"dateparse: '{AsText(value)}' does not match {(formats is null ? "a known date format" : string.Join(" | ", formats))}"),
        };
    }

    private static FieldFilter Timezone(IReadOnlyList<string> args)
    {
        Expect(args, 1);
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(args[0], out var timezone))
        {
            throw new ArgumentException($"timezone: unknown timezone '{args[0]}'");
        }

        return value =>
        {
            if (value is null)
            {
                return null;
            }

            var date = value as DateTime?;
            if (date is null)
            {
                date = FieldValues.TryParseDate(AsText(value).Trim(), null, out var parsed)
                    ? parsed
                    : throw new FieldException($"timezone: '{AsText(value)}' is not a date");
            }

            return FieldValues.ToUtc(date.Value, timezone);
        };
    }

    private static readonly Regex AgoPattern = new(
        @"(?<n>\d+(?:\.\d+)?|an?|one)\s*(?<unit>s|sec|secs|seconds?|mins?|minutes?|h|hrs?|hours?|d|days?|w|wks?|weeks?|mo|months?|y|yrs?|years?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    /// <summary>
    /// Relative dates: <c>now</c>, <c>today</c>, <c>yesterday</c>, <c>tomorrow</c>, <c>2 hours ago</c>, <c>in 3 days</c>,
    /// <c>1 day 4 hours ago</c>. <c>fuzzytime</c> also accepts absolute dates.
    /// </summary>
    private static FieldFilter TimeAgo(IReadOnlyList<string> args, TimeProvider time, bool fuzzy)
    {
        Expect(args, 0);
        return value =>
        {
            if (value is null)
            {
                return null;
            }

            if (value is DateTime date)
            {
                return date;
            }

            var text = AsText(value).Trim();
            if (TryParseRelative(text, time.GetUtcNow().UtcDateTime, out var result))
            {
                return result;
            }

            if (fuzzy && FieldValues.TryParseDate(text, null, out result))
            {
                return result;
            }

            throw new FieldException($"{(fuzzy ? "fuzzytime" : "timeago")}: cannot parse '{text}'");
        };
    }

    private static bool TryParseRelative(string text, DateTime now, out DateTime result)
    {
        var lower = text.ToLowerInvariant();
        switch (lower)
        {
            case "now" or "just now":
                result = now;
                return true;
            case "today":
                result = now.Date;
                return true;
            case "yesterday":
                result = now.Date.AddDays(-1);
                return true;
            case "tomorrow":
                result = now.Date.AddDays(1);
                return true;
        }

        var matches = AgoPattern.Matches(lower);
        if (matches.Count == 0)
        {
            result = default;
            return false;
        }

        var offset = TimeSpan.Zero;
        var months = 0;
        foreach (Match match in matches)
        {
            var n = match.Groups["n"].Value is "a" or "an" or "one" ? 1 : double.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
            var unit = match.Groups["unit"].Value;
            switch (unit[0])
            {
                case 's':
                    offset += TimeSpan.FromSeconds(n);
                    break;
                case 'h':
                    offset += TimeSpan.FromHours(n);
                    break;
                case 'd':
                    offset += TimeSpan.FromDays(n);
                    break;
                case 'w':
                    offset += TimeSpan.FromDays(7 * n);
                    break;
                case 'y':
                    months += (int)(12 * n);
                    break;
                default:
                    if (unit.StartsWith("mo", StringComparison.Ordinal))
                    {
                        months += (int)n;
                    }
                    else
                    {
                        offset += TimeSpan.FromMinutes(n);
                    }

                    break;
            }
        }

        var future = lower.StartsWith("in ", StringComparison.Ordinal) || lower.EndsWith("from now", StringComparison.Ordinal);
        result = future ? now.Add(offset).AddMonths(months) : now.Subtract(offset).AddMonths(-months);
        return true;
    }

    private static FieldFilter QueryString(IReadOnlyList<string> args)
    {
        Expect(args, 1);
        var name = args[0];
        return value => MapNullable(value, s =>
        {
            var query = s.IndexOf('?', StringComparison.Ordinal) is var i and >= 0 ? s[(i + 1)..] : s;
            var fragment = query.IndexOf('#', StringComparison.Ordinal);
            if (fragment >= 0)
            {
                query = query[..fragment];
            }

            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (WebUtility.UrlDecode(parts[0]) == name)
                {
                    return parts.Length > 1 ? WebUtility.UrlDecode(parts[1]) : "";
                }
            }

            return null;
        });
    }

    private static readonly char[] ValidateSeparators = [',', '|', '/', ';'];

    /// <summary>Keeps only the comma-separated words of the value that appear in the allowed list.</summary>
    private static FieldFilter Validate(IReadOnlyList<string> args)
    {
        var allowed = args.SelectMany(a => a.Split(ValidateSeparators))
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0)
        {
            throw new ArgumentException("validate: expected a list of allowed values");
        }

        return value => MapNullable(value, s =>
        {
            var kept = s.Split(ValidateSeparators).Select(p => p.Trim()).Where(allowed.Contains).ToList();
            return kept.Count > 0 ? string.Join(", ", kept) : null;
        });
    }

    private static FieldFilter JsonJoinArray(IReadOnlyList<string> args)
    {
        Expect(args, 2);
        if (!JsonPath.TryParse(args[0], out var path))
        {
            throw new ArgumentException($"jsonjoinarray: invalid JSONPath '{args[0]}'");
        }

        var separator = args[1];
        return value =>
        {
            if (value is null)
            {
                return null;
            }

            JsonNode? node;
            try
            {
                node = value as JsonNode ?? JsonNode.Parse(AsText(value));
            }
            catch (System.Text.Json.JsonException e)
            {
                throw new FieldException($"jsonjoinarray: value is not JSON ({e.Message})");
            }

            var matches = path.Evaluate(node).Matches;
            IEnumerable<JsonNode?> items = matches.Count == 1 && matches[0].Value is JsonArray array ? array : matches.Select(m => m.Value);
            var texts = items.Select(FieldValues.FromJson).Select(FieldValues.ToText).Where(t => !string.IsNullOrEmpty(t)).ToList();
            return texts.Count > 0 ? string.Join(separator, texts) : null;
        };
    }

    private static FieldFilter ToInt(IReadOnlyList<string> args)
    {
        Expect(args, 0);
        return value => value switch
        {
            null => null,
            long l => l,
            int i => (long)i,
            double d => (long)Math.Truncate(d),
            _ => long.TryParse(AsText(value).Trim(), NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result)
                ? result
                : throw new FieldException($"toint: '{AsText(value)}' is not an integer"),
        };
    }

    private static FieldFilter ToFloat(IReadOnlyList<string> args)
    {
        Expect(args, 0);
        return value => value switch
        {
            null => null,
            double d => d,
            long l => (double)l,
            int i => (double)i,
            _ => double.TryParse(AsText(value).Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result)
                ? result
                : throw new FieldException($"tofloat: '{AsText(value)}' is not a number"),
        };
    }

    private static object? Map(object? value, Func<string, string> transform) => value is null ? null : transform(AsText(value));

    private static object? MapNullable(object? value, Func<string, string?> transform) => value is null ? null : transform(AsText(value));

    private static string AsText(object value) => FieldValues.ToText(value) ?? "";

    private static Regex CreateRegex(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException($"invalid regular expression '{pattern}': {e.Message}");
        }
    }

    private static int ParseIntArg(string arg, string filter) =>
        int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new ArgumentException($"{filter}: '{arg}' is not an integer");

    private static IReadOnlyList<string> ArgsUpTo(IReadOnlyList<string> args, int max) =>
        args.Count <= max ? args : throw new ArgumentException($"expected at most {max} argument(s), got {args.Count}");

    private static void Expect(IReadOnlyList<string> args, int count)
    {
        if (args.Count != count)
        {
            throw new ArgumentException($"expected {count} argument(s), got {args.Count}");
        }
    }
}

/// <summary>A field value could not be extracted or converted (an item error, not a definition error).</summary>
public sealed class FieldException(string message) : Exception(message);
