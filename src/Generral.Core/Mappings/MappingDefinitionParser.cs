using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Generral.Core.Extensions;
using Generral.Core.Sources.Extraction;

namespace Generral.Core.Mappings;

/// <summary>
/// Reads a <see cref="MappingDefinition"/> from YAML or JSON. Strict: unknown keys are errors. <c>name</c>, <c>series</c>,
/// <c>source</c> and <c>priority</c> are columns of <see cref="Mapping"/> and are ignored here.
/// </summary>
public static class MappingDefinitionParser
{
    private static readonly string[] RootKeys =
        ["name", "series", "source", "priority", "filter", "vars", "dedupe", "parts", "season", "episode", "absolute", "title", "overview", "airDate", "runtime", "images", "search"];
    private static readonly string[] SeasonKeys = ["strategy", "value", "group", "title"];
    private static readonly string[] EpisodeKeys = ["strategy", "order", "value", "onInsert"];
    private static readonly string[] ImageKeys = ["thumb"];
    private static readonly string[] SearchTemplateKeys = ["queries", "must", "mustNot", "any"];
    private static readonly string[] SearchSettingKeys = ["categories", "queryMode", "dateWindow"];

    /// <exception cref="MappingDefinitionException">Invalid YAML or definition.</exception>
    public static MappingDefinition FromYaml(string yaml)
    {
        JsonNode? node;
        try
        {
            node = YamlJson.Parse(yaml);
        }
        catch (FormatException e)
        {
            throw new MappingDefinitionException(e.Message);
        }

        return FromJson(node);
    }

    /// <exception cref="MappingDefinitionException">Invalid JSON or definition.</exception>
    public static MappingDefinition FromJson(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException e)
        {
            throw new MappingDefinitionException($"Invalid JSON: {e.Message}");
        }

        return FromJson(node);
    }

    /// <exception cref="MappingDefinitionException">Invalid definition.</exception>
    public static MappingDefinition FromJson(JsonNode? node)
    {
        var root = Strict(node, "mapping", RootKeys);
        var season = ParseSeason(root["season"]);
        var episode = ParseEpisode(root["episode"]);
        var parts = ParseParts(root["parts"]);
        var search = ParseSearch(root["search"]);
        if (search.Parts.Keys.FirstOrDefault(k => parts?.Any(p => p.Key == k) != true) is { } unknown)
        {
            throw Error("search.parts", $"unknown part '{unknown}'");
        }

        return new MappingDefinition
        {
            Filter = Templates(root["filter"], "filter"),
            Vars = ParseVars(root["vars"]),
            Dedupe = Template(root["dedupe"], "dedupe"),
            Parts = parts,
            Season = season,
            Episode = episode,
            Absolute = Template(root["absolute"], "absolute"),
            Title = Template(root["title"], "title") ?? (parts is null ? MappingDefinition.DefaultTitle : MappingDefinition.DefaultPartTitle),
            Overview = Template(root["overview"], "overview"),
            AirDate = Template(root["airDate"], "airDate") ?? MappingDefinition.DefaultAirDate,
            Runtime = Template(root["runtime"], "runtime"),
            Thumb = root["images"] is { } images ? Template(Strict(images, "images", ImageKeys)["thumb"], "images.thumb") : null,
            Search = search,
        };
    }

    /// <summary>A list of parts; keys other than <c>key</c>, <c>when</c>, <c>monitored</c>, <c>airDate</c>, <c>runtime</c> and <c>search</c> are free (<c>part.*</c>).</summary>
    private static List<PartDefinition>? ParseParts(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject)
        {
            throw Error("parts", "'from' is not supported yet");
        }

        if (node is not JsonArray { Count: > 0 } array)
        {
            throw Error("parts", "must be a non-empty list of parts");
        }

        var parts = new List<PartDefinition>();
        for (var i = 0; i < array.Count; i++)
        {
            var location = $"parts[{i}]";
            var part = ParsePart(AsObject(array[i], location), location);
            if (!IsPartKey(part.Key))
            {
                throw Error($"{location}.key", "must be letters, digits, '-' and '_'");
            }

            if (parts.Any(p => p.Key == part.Key))
            {
                throw Error($"{location}.key", $"'{part.Key}' is used twice");
            }

            parts.Add(part);
        }

        return parts;
    }

    private static PartDefinition ParsePart(JsonObject obj, string location)
    {
        var values = new JsonObject();
        foreach (var (name, value) in obj)
        {
            if (name != "search")
            {
                values[name] = value?.DeepClone();
            }
        }

        return new PartDefinition
        {
            Key = Template(obj["key"], $"{location}.key") ?? throw Error($"{location}.key", "is required"),
            When = Template(obj["when"], $"{location}.when"),
            Monitored = Template(obj["monitored"], $"{location}.monitored"),
            AirDate = Template(obj["airDate"], $"{location}.airDate"),
            Runtime = Template(obj["runtime"], $"{location}.runtime"),
            Search = ParsePartSearch(obj["search"], $"{location}.search"),
            Values = values,
        };
    }

    private static PartSearchDefinition ParsePartSearch(JsonNode? node, string location)
    {
        if (node is null)
        {
            return new PartSearchDefinition();
        }

        var obj = Strict(node, location, SearchTemplateKeys);
        return new PartSearchDefinition
        {
            Queries = Templates(obj["queries"], $"{location}.queries"),
            Must = Templates(obj["must"], $"{location}.must"),
            MustNot = Templates(obj["mustNot"], $"{location}.mustNot"),
            Any = Templates(obj["any"], $"{location}.any"),
        };
    }

    private static List<KeyValuePair<string, string>> ParseVars(JsonNode? node)
    {
        if (node is null)
        {
            return [];
        }

        var vars = new List<KeyValuePair<string, string>>();
        foreach (var (name, value) in AsObject(node, "vars"))
        {
            if (!IsIdentifier(name))
            {
                throw Error("vars", $"'{name}' is not a valid name (letters, digits and _, not starting with a digit)");
            }

            vars.Add(KeyValuePair.Create(name, Template(value, $"vars.{name}") ?? ""));
        }

        return vars;
    }

    private static SeasonDefinition ParseSeason(JsonNode? node)
    {
        if (node is null)
        {
            return new SeasonDefinition();
        }

        var obj = Strict(node, "season", SeasonKeys);
        var season = new SeasonDefinition
        {
            Strategy = Enum<SeasonStrategy>(obj["strategy"], "season.strategy") ?? SeasonStrategy.Year,
            Value = Template(obj["value"], "season.value"),
            Group = Template(obj["group"], "season.group"),
            Title = Template(obj["title"], "season.title"),
        };

        switch (season.Strategy)
        {
            case SeasonStrategy.Field when season.Value is null:
                throw Error("season.value", "is required with strategy 'field'");
            case SeasonStrategy.Fixed when !int.TryParse(season.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 0:
                throw Error("season.value", "must be a season number with strategy 'fixed'");
            case SeasonStrategy.Sequential when season.Group is null:
                throw Error("season.group", "is required with strategy 'sequential'");
        }

        return season;
    }

    private static EpisodeDefinition ParseEpisode(JsonNode? node)
    {
        if (node is null)
        {
            return new EpisodeDefinition();
        }

        var obj = Strict(node, "episode", EpisodeKeys);
        var episode = new EpisodeDefinition
        {
            Strategy = Enum<EpisodeStrategy>(obj["strategy"], "episode.strategy") ?? EpisodeStrategy.Sequential,
            Order = Template(obj["order"], "episode.order") ?? EpisodeDefinition.DefaultOrder,
            Value = Template(obj["value"], "episode.value"),
            OnInsert = Enum<InsertPolicy>(obj["onInsert"], "episode.onInsert") ?? InsertPolicy.Append,
        };

        if (episode.Strategy == EpisodeStrategy.Field && episode.Value is null)
        {
            throw Error("episode.value", "is required with strategy 'field'");
        }

        return episode;
    }

    private static SearchDefinition ParseSearch(JsonNode? node)
    {
        if (node is null)
        {
            return new SearchDefinition();
        }

        var obj = Strict(node, "search", [.. SearchTemplateKeys, .. SearchSettingKeys, "parts", "pack"]);
        if (obj["pack"] is not null)
        {
            throw Error("search", "'pack' is not supported yet");
        }

        var settings = new JsonObject();
        foreach (var key in SearchSettingKeys)
        {
            if (obj[key] is { } value)
            {
                settings[key] = value.DeepClone();
            }
        }

        return new SearchDefinition
        {
            Queries = Templates(obj["queries"], "search.queries"),
            Must = Templates(obj["must"], "search.must"),
            MustNot = Templates(obj["mustNot"], "search.mustNot"),
            Any = Templates(obj["any"], "search.any"),
            Parts = obj["parts"] is { } parts
                ? AsObject(parts, "search.parts").ToDictionary(p => p.Key, p => ParsePartSearch(p.Value, $"search.parts.{p.Key}"))
                : new Dictionary<string, PartSearchDefinition>(),
            Settings = settings,
        };
    }

    private static List<string> Templates(JsonNode? node, string location) => node switch
    {
        null => [],
        JsonArray array => array.Select((t, i) => Template(t, $"{location}[{i}]") ?? throw Error($"{location}[{i}]", "must not be null")).ToList(),
        JsonValue => [Template(node, location)!],
        _ => throw Error(location, "must be a list of strings"),
    };

    /// <summary>A string; numbers and booleans are accepted as constant templates.</summary>
    private static string? Template(JsonNode? node, string location) => node switch
    {
        null => null,
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        JsonValue value => FieldValues.ToText(FieldValues.FromJson(value)),
        _ => throw Error(location, "must be a string"),
    };

    private static T? Enum<T>(JsonNode? node, string location)
        where T : struct, Enum
    {
        if (node is null)
        {
            return null;
        }

        var text = Template(node, location)!;
        var names = System.Enum.GetNames<T>().Select(KebabCase).ToList();
        var index = names.IndexOf(text);
        return index >= 0
            ? System.Enum.GetValues<T>()[index]
            : throw Error(location, $"unknown value '{text}' (expected {string.Join(", ", names)})");
    }

    private static string KebabCase(string name) =>
        string.Concat(name.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "-" : "") + char.ToLowerInvariant(c) : c.ToString()));

    private static bool IsIdentifier(string name) =>
        name.Length > 0 && !char.IsAsciiDigit(name[0]) && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static bool IsPartKey(string key) =>
        key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static JsonObject Strict(JsonNode? node, string location, string[] allowed)
    {
        var obj = AsObject(node, location);
        foreach (var key in obj.Select(p => p.Key))
        {
            if (!allowed.Contains(key))
            {
                throw Error(location, $"unknown property '{key}' (expected {string.Join(", ", allowed)})");
            }
        }

        return obj;
    }

    private static JsonObject AsObject(JsonNode? node, string location) =>
        node as JsonObject ?? throw Error(location, "must be an object");

    private static MappingDefinitionException Error(string location, string message) => new($"{location}: {message}");
}
