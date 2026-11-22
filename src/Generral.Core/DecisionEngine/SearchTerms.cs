using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Generral.Core.Templating;

namespace Generral.Core.DecisionEngine;

public enum QueryMode
{
    /// <summary>Queries are tried in order until one returns results.</summary>
    FirstWithResults,

    /// <summary>Every query runs; results are merged.</summary>
    All,
}

/// <summary>An episode's rendered search settings (<c>Episode.SearchTerms</c> over the series' <c>searchSettings</c>), see spec 06 § Per-series search settings.</summary>
public sealed record SearchTerms
{
    public static readonly TimeSpan DefaultBefore = TimeSpan.FromDays(1);

    public static readonly TimeSpan DefaultAfter = TimeSpan.FromDays(365);

    public IReadOnlyList<string> Queries { get; init; } = [];

    public IReadOnlyList<string> Must { get; init; } = [];

    public IReadOnlyList<string> MustNot { get; init; } = [];

    public IReadOnlyList<string> Any { get; init; } = [];

    /// <summary>Null for the indexer's categories.</summary>
    public IReadOnlyList<int>? Categories { get; init; }

    public QueryMode QueryMode { get; init; } = QueryMode.FirstWithResults;

    public TimeSpan Before { get; init; } = DefaultBefore;

    public TimeSpan After { get; init; } = DefaultAfter;

    /// <summary>Episode keys win over the series defaults, key by key.</summary>
    public static SearchTerms From(JsonObject episodeTerms, JsonObject? seriesDefaults = null)
    {
        JsonNode? Get(string key) => episodeTerms[key] ?? seriesDefaults?[key];

        var window = Get("dateWindow") as JsonObject;
        return new SearchTerms
        {
            Queries = Strings(Get("queries")),
            Must = Strings(Get("must")),
            MustNot = Strings(Get("mustNot")),
            Any = Strings(Get("any")),
            Categories = Get("categories") is JsonArray cats
                ? cats.OfType<JsonValue>().Select(c => c.TryGetValue<int>(out var id) ? id : (int?)null).OfType<int>().ToList()
                : null,
            QueryMode = Get("queryMode") is JsonValue mode && mode.TryGetValue<string>(out var text) && text == "all" ? QueryMode.All : QueryMode.FirstWithResults,
            Before = Duration(window?["before"]) ?? DefaultBefore,
            After = Duration(window?["after"]) ?? DefaultAfter,
        };
    }

    private static List<string> Strings(JsonNode? node) =>
        node is JsonArray array ? array.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList() : [];

    private static TimeSpan? Duration(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && GenerralTemplateHelpers.TryParseDuration(text, out var duration) ? duration.Duration() : null;
}

/// <summary>Evaluates <c>must</c>/<c>mustNot</c>/<c>any</c> regexes against a release title.</summary>
public static partial class ReleaseTitleMatcher
{
    public const string RawPrefix = "raw:";

    private static readonly ConcurrentDictionary<string, Regex?> Cache = new();

    /// <summary><c>.</c>, <c>_</c> and <c>+</c> become spaces, spaces are collapsed and diacritics removed.</summary>
    public static string Normalize(string title)
    {
        var builder = new StringBuilder(title.Length);
        foreach (var c in title.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c is '.' or '_' or '+' ? ' ' : c);
            }
        }

        return Spaces().Replace(builder.ToString(), " ").Trim().Normalize(NormalizationForm.FormC);
    }

    /// <returns>Why the title doesn't match the rules, or null when it does.</returns>
    public static string? Check(SearchTerms terms, string title)
    {
        var normalized = Normalize(title);
        foreach (var rule in terms.Must)
        {
            if (Match(rule, title, normalized) is var (ok, error) && !ok)
            {
                return error ?? $"Does not match required rule '{rule}'";
            }
        }

        foreach (var rule in terms.MustNot)
        {
            if (Match(rule, title, normalized) is var (ok, error) && (ok || error != null))
            {
                return error ?? $"Matches excluded rule '{rule}'";
            }
        }

        if (terms.Any.Count > 0 && !terms.Any.Any(rule => Match(rule, title, normalized).Ok))
        {
            return $"Does not match any of the rules {string.Join(", ", terms.Any.Select(r => $"'{r}'"))}";
        }

        return null;
    }

    private static (bool Ok, string? Error) Match(string rule, string raw, string normalized)
    {
        var isRaw = rule.StartsWith(RawPrefix, StringComparison.Ordinal);
        var pattern = isRaw ? rule[RawPrefix.Length..] : rule;
        var regex = Cache.GetOrAdd(pattern, static p =>
        {
            try
            {
                return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException)
            {
                return null;
            }
        });

        if (regex is null)
        {
            return (false, $"Invalid search rule '{rule}'");
        }

        try
        {
            return (regex.IsMatch(isRaw ? raw : normalized), null);
        }
        catch (RegexMatchTimeoutException)
        {
            return (false, $"Search rule '{rule}' timed out");
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
