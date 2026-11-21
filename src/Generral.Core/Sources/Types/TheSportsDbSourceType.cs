using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Http;

namespace Generral.Core.Sources.Types;

/// <summary>
/// TheSportsDB v1 API: a league's seasons (<c>eventsseason</c>), topped up with <c>eventspastleague</c> /
/// <c>eventsnextleague</c>. The league (<c>lookupleague</c>) becomes the source meta. All times are UTC.
/// </summary>
public sealed partial class TheSportsDbSourceType(SourceFetcherFactory fetchers) : SourceType<TheSportsDbSourceType.Config?>(fetchers)
{
    public const string BaseUrl = "https://www.thesportsdb.com/api/v1/json/";
    public const string FreeApiKey = "123";

    /// <summary>The free key returns at most this many events per season.</summary>
    public const int FreeSeasonCap = 15;

    public const int MaxSeasons = 50;

    public override string Id => "thesportsdb";

    public override string Name => "TheSportsDB";

    protected override TimeSpan DefaultRequestDelay => TimeSpan.FromSeconds(2);

    protected override bool UsesTimezone => false;

    protected override IReadOnlyList<SourceField> TypeFields { get; } =
    [
        new()
        {
            Name = "apiKey",
            Label = "API Key",
            Type = SourceFieldType.Password,
            Value = FreeApiKey,
            HelpText = $"{FreeApiKey} is the free key: it returns at most {FreeSeasonCap} events per season. A premium key lifts the limit",
        },
        new()
        {
            Name = "leagueId",
            Label = "League ID",
            Placeholder = "4443",
            HelpText = "The idLeague of the league, e.g. 4443 for UFC",
        },
        new()
        {
            Name = "seasons",
            Label = "Seasons",
            Value = "current",
            HelpText = "all, current, a list (2024, 2024-2025) or a range up to now (2023..current)",
        },
        new()
        {
            Name = "includePast",
            Label = "Include Past Events",
            Type = SourceFieldType.Checkbox,
            Value = true,
            HelpText = "Also fetch the league's latest results (eventspastleague)",
            Advanced = true,
        },
        new()
        {
            Name = "includeFuture",
            Label = "Include Upcoming Events",
            Type = SourceFieldType.Checkbox,
            Value = true,
            HelpText = "Also fetch the league's next events (eventsnextleague)",
            Advanced = true,
        },
    ];

    protected override Config? Read(SourceSettings settings)
    {
        var apiKey = settings.String("apiKey") ?? FreeApiKey;
        if (!ApiKey().IsMatch(apiKey))
        {
            settings.Fail("apiKey", "must only contain letters and digits");
        }

        var leagueId = settings.String("leagueId", required: true);
        if (leagueId is not null && !LeagueId().IsMatch(leagueId))
        {
            settings.Fail("leagueId", "must be a number");
        }

        var seasonsText = settings["seasons"] is JsonArray list
            ? string.Join(',', list.Select(s => FieldValues.ToText(FieldValues.FromJson(s))))
            : settings.String("seasons") ?? "current";
        var seasons = ParseSeasons(seasonsText, out var error);
        if (error is not null)
        {
            settings.Fail("seasons", error);
        }

        var includePast = settings.Bool("includePast", true);
        var includeFuture = settings.Bool("includeFuture", true);
        return settings.Failures.Count > 0 ? null : new Config(apiKey, leagueId!, seasons!, includePast, includeFuture);
    }

    protected override async Task<SourceFetchResult> FetchAsync(Config? config, SourceFetchContext context, CancellationToken cancellationToken)
    {
        var api = new Api(config!, context.Fetcher, cancellationToken);
        var warnings = new List<string>();

        var league = (await api.GetAsync("lookupleague.php", null))["leagues"] is JsonArray { Count: > 0 } leagues && leagues[0] is JsonObject found
            ? found
            : throw new SourceFetchException($"League {config!.LeagueId} not found");
        var currentSeason = Text(league["strCurrentSeason"]);

        var seasons = await ResolveSeasonsAsync(config!.Seasons, currentSeason, api, warnings);
        var events = new List<(JsonObject Event, Uri Url)>();
        foreach (var season in seasons)
        {
            var (url, list) = await api.GetEventsAsync("eventsseason.php", season);
            if (list.Count == 0)
            {
                warnings.Add($"Season {season} returned no events");
            }
            else if (list.Count == FreeSeasonCap)
            {
                warnings.Add($"Season {season} returned exactly {FreeSeasonCap} events, the free API key limit: some events are probably missing. Use a premium key, or combine with another source");
            }

            events.AddRange(list.Select(e => (e, url)));
        }

        foreach (var (enabled, endpoint) in new[] { (config.IncludePast, "eventspastleague.php"), (config.IncludeFuture, "eventsnextleague.php") })
        {
            if (enabled)
            {
                var (url, list) = await api.GetEventsAsync(endpoint, null);
                events.AddRange(list.Select(e => (e, url)));
            }
        }

        var keys = new HashSet<string>();
        var items = new List<ExtractedItem>();
        foreach (var (item, url) in events)
        {
            if (Text(item["idEvent"]) is { } id && !keys.Add(id))
            {
                continue;
            }

            items.Add(ToItem(item, items.Count, url));
        }

        return Result(context, items, Meta(league), warnings);
    }

    /// <summary>Season names to fetch, in order. <c>all</c> and ranges read the league's season list.</summary>
    private static async Task<IReadOnlyList<string>> ResolveSeasonsAsync(IReadOnlyList<SeasonSpec> specs, string? currentSeason, Api api, List<string> warnings)
    {
        List<string>? available = null;
        var result = new List<string>();
        foreach (var spec in specs)
        {
            if (spec.Name == "current")
            {
                if (currentSeason is null)
                {
                    warnings.Add("The league has no current season");
                    continue;
                }

                result.Add(currentSeason);
            }
            else if (spec.Name is not null)
            {
                result.Add(spec.Name);
            }
            else
            {
                available ??= await api.GetSeasonsAsync();
                var to = spec.To ?? StartYear(currentSeason) ?? int.MaxValue;
                result.AddRange(available.Where(s => StartYear(s) is { } year && year >= spec.From && year <= to));
            }
        }

        result = result.Distinct().ToList();
        if (result.Count > MaxSeasons)
        {
            warnings.Add($"Only the last {MaxSeasons} of {result.Count} seasons are fetched");
            result = result[^MaxSeasons..];
        }

        return result;
    }

    private static ExtractedItem ToItem(JsonObject item, int index, Uri url)
    {
        var fields = (JsonObject)item.DeepClone();
        var start = Start(item);
        var values = new Dictionary<string, object?>
        {
            [ReservedFields.Key] = Text(item["idEvent"]),
            [ReservedFields.Title] = Text(item["strEvent"]),
            [ReservedFields.Start] = start,
            [ReservedFields.Status] = Text(item["strStatus"]),
            ["poster"] = Text(item["strPoster"]),
            ["fanart"] = Text(item["strFanart"]),
            ["thumb"] = Text(item["strThumb"]),
            ["banner"] = Text(item["strBanner"]),
        };
        foreach (var name in new[] { ReservedFields.Key, ReservedFields.Title, ReservedFields.Start, ReservedFields.Status })
        {
            fields[name] = FieldValues.ToJson(values[name]);
        }

        var errors = new List<string>();
        var extracted = ReservedFields.ToEvent(values, fields, TimeZoneInfo.Utc, url, errors);
        return new ExtractedItem(index, url, fields, extracted, errors);
    }

    /// <summary><c>strTimestamp</c>, or <c>dateEvent</c> + <c>strTime</c>, in UTC.</summary>
    private static DateTime? Start(JsonObject item)
    {
        var text = Text(item["strTimestamp"]);
        if (text is null && Text(item["dateEvent"]) is { } date)
        {
            text = Text(item["strTime"]) is { } time ? $"{date}T{time}" : date;
        }

        return text is not null && FieldValues.TryParseDate(text, null, out var parsed) ? FieldValues.ToUtc(parsed, TimeZoneInfo.Utc) : null;
    }

    /// <summary>The league object, with <c>strFanart</c> defaulting to the first of <c>strFanart1..4</c>.</summary>
    private static JsonObject Meta(JsonObject league)
    {
        var meta = (JsonObject)league.DeepClone();
        if (Text(meta["strFanart"]) is null && Text(meta["strFanart1"]) is { } fanart)
        {
            meta["strFanart"] = fanart;
        }

        return meta;
    }

    private static string? Text(JsonNode? node) => FieldValues.ToText(FieldValues.FromJson(node)) is { } text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static int? StartYear(string? season) =>
        season is not null && SeasonYear().Match(season) is { Success: true } match ? int.Parse(match.Value, CultureInfo.InvariantCulture) : null;

    internal static IReadOnlyList<SeasonSpec>? ParseSeasons(string text, out string? error)
    {
        error = null;
        var specs = new List<SeasonSpec>();
        foreach (var token in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                specs.Add(new SeasonSpec(null, int.MinValue, int.MaxValue));
            }
            else if (token.Equals("current", StringComparison.OrdinalIgnoreCase))
            {
                specs.Add(new SeasonSpec("current", 0, null));
            }
            else if (SeasonRange().Match(token) is { Success: true } range)
            {
                var from = int.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture);
                int? to = range.Groups[2].Value.Equals("current", StringComparison.OrdinalIgnoreCase) ? null : int.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture);
                if (to < from)
                {
                    error = $"'{token}': the range ends before it starts";
                    return null;
                }

                specs.Add(new SeasonSpec(null, from, to));
            }
            else if (SeasonName().IsMatch(token))
            {
                specs.Add(new SeasonSpec(token, 0, null));
            }
            else
            {
                error = $"'{token}' is not a season (use all, current, 2024, 2024-2025 or 2023..current)";
                return null;
            }
        }

        if (specs.Count == 0)
        {
            error = "is required";
            return null;
        }

        return specs;
    }

    [GeneratedRegex("^[A-Za-z0-9]+$")]
    private static partial Regex ApiKey();

    [GeneratedRegex("^[0-9]+$")]
    private static partial Regex LeagueId();

    [GeneratedRegex(@"^([0-9]{4})\s*\.\.\s*([0-9]{4}|current)$", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonRange();

    [GeneratedRegex(@"^[0-9A-Za-z][0-9A-Za-z /_-]*$")]
    private static partial Regex SeasonName();

    [GeneratedRegex("[0-9]{4}")]
    private static partial Regex SeasonYear();

    public sealed record Config(string ApiKey, string LeagueId, IReadOnlyList<SeasonSpec> Seasons, bool IncludePast, bool IncludeFuture);

    /// <summary>A named season (or <c>current</c>), or a range of start years when <see cref="Name"/> is null (<see cref="To"/> null = current).</summary>
    public sealed record SeasonSpec(string? Name, int From, int? To);

    private sealed class Api(Config config, SourceDocumentFetcher fetcher, CancellationToken cancellationToken)
    {
        public async Task<JsonObject> GetAsync(string endpoint, string? season) => (await GetWithUrlAsync(endpoint, season)).Document;

        public async Task<(Uri Url, List<JsonObject> Events)> GetEventsAsync(string endpoint, string? season)
        {
            var (url, document) = await GetWithUrlAsync(endpoint, season);
            return (url, document["events"] is JsonArray events ? events.OfType<JsonObject>().ToList() : []);
        }

        public async Task<List<string>> GetSeasonsAsync()
        {
            var document = await GetAsync("search_all_seasons.php", null);
            return document["seasons"] is JsonArray seasons
                ? seasons.Select(s => Text(s?["strSeason"])).OfType<string>().ToList()
                : [];
        }

        private async Task<(Uri Url, JsonObject Document)> GetWithUrlAsync(string endpoint, string? season)
        {
            var query = $"id={Uri.EscapeDataString(config.LeagueId)}" + (season is null ? "" : $"&s={Uri.EscapeDataString(season)}");
            var url = new Uri($"{BaseUrl}{Uri.EscapeDataString(config.ApiKey)}/{endpoint}?{query}");
            var document = await fetcher.FetchAsync(url, cancellationToken);
            try
            {
                return (url, JsonNode.Parse(document.Content) as JsonObject ?? throw new JsonException("not an object"));
            }
            catch (JsonException e)
            {
                throw new SourceFetchException($"{endpoint}: invalid response ({e.Message})", innerException: e);
            }
        }
    }
}
