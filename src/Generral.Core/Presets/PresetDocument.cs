using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Generral.Core.Extensions;
using Generral.Core.Mappings;
using Generral.Core.Templating;
using Generral.Core.Tv;

namespace Generral.Core.Presets;

public sealed class PresetException(string message) : Exception(message);

/// <summary>A value prompted when a preset is instantiated, referenced as <c>{{ NAME }}</c> (or <c>{{ secret.NAME }}</c> when secret).</summary>
public sealed record PresetParameter(string Name, string Label, string? Default, bool Secret);

public sealed record PresetSource(string Name, string Type, JsonObject Settings, TimeSpan? RefreshInterval);

/// <summary>Series defaults; unset values keep the series defaults.</summary>
public sealed record PresetSeries
{
    public string Title { get; init; } = "";

    public string? Overview { get; init; }

    public string? Network { get; init; }

    public List<string> Genres { get; init; } = [];

    public int? Year { get; init; }

    public SeriesType? SeriesType { get; init; }

    public bool? SeasonFolder { get; init; }

    public bool? Monitored { get; init; }

    public NewItemMonitorType? MonitorNewItems { get; init; }

    public JsonObject? SearchSettings { get; init; }
}

/// <summary><see cref="Definition"/> is the mapping without <c>name</c>, <c>priority</c> and <c>source</c>.</summary>
public sealed record PresetMapping(string Name, int Priority, PresetSource Source, JsonObject Definition, MappingDefinition Parsed);

/// <summary>A preset with its placeholders replaced.</summary>
public sealed record ResolvedPreset(IReadOnlyList<PresetSource> Sources, PresetSeries Series, IReadOnlyList<PresetMapping> Mappings);

/// <summary>
/// A <c>generral/preset@1</c> document (docs/specs/05-mapping.md § Presets): <c>source</c> (or <c>sources</c>), <c>series</c>
/// defaults and <c>mapping</c> (or <c>mappings</c>). Placeholders are replaced as text before anything else is read.
/// </summary>
public sealed partial class PresetDocument
{
    public const string SchemaName = "generral/preset@1";

    private static readonly string[] RootKeys =
        ["schema", "id", "name", "description", "requires", "tags", "parameters", "source", "sources", "series", "mapping", "mappings"];
    private static readonly string[] SourceKeys = ["name", "type", "settings", "refreshInterval"];
    private static readonly string[] ParameterKeys = ["name", "label", "default", "secret"];

    /// <summary>Names the mapping templates use: a parameter with one of them would shadow it.</summary>
    private static readonly HashSet<string> ReservedNames =
        ["event", "vars", "part", "series", "season", "episode", "source", "secret", "date", "string", "regex", "array", "math", "object", "sport", "request", "if", "end", "for", "else"];

    private static readonly JsonSerializerOptions SeriesJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private readonly JsonObject _root;

    private PresetDocument(JsonObject root, string text)
    {
        _root = root;
        Text = text;
        Id = RequiredString(root, "id");
        if (!IdPattern().IsMatch(Id))
        {
            throw new PresetException("id: must be lower-case letters, digits and '-'");
        }

        Name = RequiredString(root, "name");
        Description = OptionalString(root["description"], "description")?.Trim();
        Tags = root["tags"] is JsonArray tags ? tags.Select(t => OptionalString(t, "tags")).OfType<string>().ToList() : [];
        Parameters = ParseParameters(root);
        var sources = SourceNodes(root);
        SourceTypes = sources.Select(s => OptionalString(s["type"], "source.type") ?? "").Distinct().ToList();
    }

    public string Id { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Gallery labels (e.g. <c>sport</c>), not series tags.</summary>
    public IReadOnlyList<string> Tags { get; }

    public IReadOnlyList<PresetParameter> Parameters { get; }

    public IReadOnlyList<string> SourceTypes { get; }

    /// <summary>Secrets without a default: they must be given to instantiate the preset.</summary>
    public IReadOnlyList<string> RequiredSecrets => Parameters.Where(p => p.Secret && p.Default is null).Select(p => p.Name).ToList();

    /// <summary>The document as authored.</summary>
    public string Text { get; }

    /// <exception cref="PresetException">Invalid YAML or preset.</exception>
    public static PresetDocument Parse(string text)
    {
        JsonNode? node;
        try
        {
            node = YamlJson.Parse(text);
        }
        catch (FormatException e)
        {
            throw new PresetException(e.Message);
        }

        if (node is not JsonObject root)
        {
            throw new PresetException("preset: must be an object");
        }

        if (root.Select(p => p.Key).FirstOrDefault(k => !RootKeys.Contains(k)) is { } unknown)
        {
            throw new PresetException($"preset: unknown property '{unknown}' (expected {string.Join(", ", RootKeys)})");
        }

        if (OptionalString(root["schema"], "schema") != SchemaName)
        {
            throw new PresetException($"schema: must be '{SchemaName}'");
        }

        return new PresetDocument(root, text);
    }

    /// <summary>Defaults, and a dummy value for missing secrets: enough to validate the document.</summary>
    public IReadOnlyDictionary<string, string> SampleValues() =>
        Parameters.ToDictionary(p => p.Name, p => p.Default ?? (p.Secret ? "placeholder" : ""));

    /// <summary>Replaces the placeholders with <paramref name="values"/> (or the defaults) and reads the preset.</summary>
    /// <exception cref="PresetException">A value is missing, or the preset is invalid.</exception>
    public ResolvedPreset Resolve(IReadOnlyDictionary<string, string?> values)
    {
        var resolved = new Dictionary<string, string>();
        foreach (var parameter in Parameters)
        {
            var value = values.GetValueOrDefault(parameter.Name);
            value = string.IsNullOrWhiteSpace(value) ? parameter.Default : value.Trim();
            resolved[parameter.Name] = value ?? throw new PresetException($"{(parameter.Secret ? "secrets" : "parameters")}.{parameter.Name}: is required");
        }

        var root = (JsonObject)Substitute(_root.DeepClone(), resolved)!;
        var sources = SourceNodes(root).Select(ParseSource).ToList();
        if (sources.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            throw new PresetException($"sources: '{duplicate.Key}' is used twice");
        }

        return new ResolvedPreset(sources, ParseSeries(root["series"]), ParseMappings(root, sources));
    }

    private static List<PresetParameter> ParseParameters(JsonObject root)
    {
        var declared = new List<PresetParameter>();
        if (root["parameters"] is { } node)
        {
            if (node is not JsonArray array)
            {
                throw new PresetException("parameters: must be a list");
            }

            foreach (var (item, index) in array.Select((p, i) => (p, i)))
            {
                var location = $"parameters[{index}]";
                if (item is not JsonObject obj || obj.Select(p => p.Key).FirstOrDefault(k => !ParameterKeys.Contains(k)) is { } unknown)
                {
                    throw new PresetException($"{location}: must be an object with {string.Join(", ", ParameterKeys)}");
                }

                var name = OptionalString(obj["name"], $"{location}.name") ?? "";
                if (!ParameterName().IsMatch(name) || ReservedNames.Contains(name))
                {
                    throw new PresetException($"{location}.name: '{name}' is not a valid parameter name");
                }

                if (declared.Any(p => p.Name == name))
                {
                    throw new PresetException($"{location}.name: '{name}' is used twice");
                }

                var secret = obj["secret"] switch
                {
                    null => false,
                    JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => v.GetValue<bool>(),
                    _ => throw new PresetException($"{location}.secret: must be true or false"),
                };
                declared.Add(new PresetParameter(name, OptionalString(obj["label"], $"{location}.label") ?? name, OptionalString(obj["default"], $"{location}.default"), secret));
            }
        }

        // Undeclared {{ secret.NAME }} placeholders are required secrets.
        foreach (Match match in Placeholder().Matches(root.ToJsonString()))
        {
            var name = match.Groups["name"].Value;
            if (match.Groups["secret"].Success && declared.All(p => p.Name != name) && !ReservedNames.Contains(name))
            {
                declared.Add(new PresetParameter(name, name, null, true));
            }
        }

        return declared;
    }

    private static JsonNode? Substitute(JsonNode? node, IReadOnlyDictionary<string, string> values)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    obj[key] = Substitute(obj[key]?.DeepClone(), values);
                }

                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    array[i] = Substitute(array[i]?.DeepClone(), values);
                }

                return array;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                var text = value.GetValue<string>();
                var replaced = Placeholder().Replace(text, m => values.TryGetValue(m.Groups["name"].Value, out var v) ? v : m.Value);
                return replaced == text ? value : JsonValue.Create(replaced);
            default:
                return node;
        }
    }

    private static List<JsonObject> SourceNodes(JsonObject root)
    {
        var list = (root["source"], root["sources"]) switch
        {
            (JsonObject one, null) => [one],
            (null, JsonArray many) when many.Count > 0 && many.All(s => s is JsonObject) => many.Cast<JsonObject>().ToList(),
            _ => throw new PresetException("source: a source object (or a non-empty 'sources' list) is required"),
        };

        foreach (var source in list)
        {
            if (source.Select(p => p.Key).FirstOrDefault(k => !SourceKeys.Contains(k)) is { } unknown)
            {
                throw new PresetException($"source: unknown property '{unknown}' (expected {string.Join(", ", SourceKeys)})");
            }
        }

        return list;
    }

    private static PresetSource ParseSource(JsonObject node)
    {
        TimeSpan? interval = null;
        if (OptionalString(node["refreshInterval"], "source.refreshInterval") is { } text)
        {
            interval = GenerralTemplateHelpers.TryParseDuration(text, out var parsed) && parsed > TimeSpan.Zero
                ? parsed
                : throw new PresetException($"source.refreshInterval: '{text}' is not a duration (e.g. 12h)");
        }

        return new PresetSource(
            RequiredString(node, "name", "source.name"),
            RequiredString(node, "type", "source.type"),
            node["settings"] switch
            {
                null => [],
                JsonObject settings => (JsonObject)settings.DeepClone(),
                _ => throw new PresetException("source.settings: must be an object"),
            },
            interval);
    }

    private static PresetSeries ParseSeries(JsonNode? node)
    {
        if (node is not JsonObject)
        {
            throw new PresetException("series: must be an object with at least a title");
        }

        PresetSeries? series;
        try
        {
            series = node.Deserialize<PresetSeries>(SeriesJson);
        }
        catch (JsonException e)
        {
            throw new PresetException($"series: {e.Message}");
        }

        return series is { Title.Length: > 0 } ? series with { Title = series.Title.Trim() } : throw new PresetException("series.title: is required");
    }

    private static List<PresetMapping> ParseMappings(JsonObject root, List<PresetSource> sources)
    {
        var nodes = (root["mapping"], root["mappings"]) switch
        {
            (JsonObject one, null) => [one],
            (null, JsonArray many) when many.Count > 0 && many.All(m => m is JsonObject) => many.Cast<JsonObject>().ToList(),
            _ => throw new PresetException("mapping: a mapping object (or a non-empty 'mappings' list) is required"),
        };

        var mappings = new List<PresetMapping>();
        foreach (var (node, index) in nodes.Select((m, i) => (m, i)))
        {
            var location = nodes.Count == 1 ? "mapping" : $"mappings[{index}]";
            var definition = (JsonObject)node.DeepClone();
            var name = OptionalString(definition["name"], $"{location}.name") ?? root["series"]?["title"]?.ToString() ?? "Mapping";
            var priority = definition["priority"] switch
            {
                null => index,
                JsonValue v when v.TryGetValue<long>(out var p) && p is >= int.MinValue and <= int.MaxValue => (int)p,
                _ => throw new PresetException($"{location}.priority: must be a whole number"),
            };
            var sourceName = OptionalString(definition["source"], $"{location}.source");
            var source = sourceName is null
                ? sources[0]
                : sources.FirstOrDefault(s => string.Equals(s.Name, sourceName, StringComparison.OrdinalIgnoreCase))
                  ?? throw new PresetException($"{location}.source: no source named '{sourceName}' in the preset");
            definition.Remove("name");
            definition.Remove("priority");
            definition.Remove("source");
            definition.Remove("series");

            MappingDefinition parsed;
            try
            {
                parsed = MappingDefinitionParser.FromJson(definition);
            }
            catch (MappingDefinitionException e)
            {
                throw new PresetException($"{location}: {e.Message}");
            }

            mappings.Add(new PresetMapping(name.Trim(), priority, source, definition, parsed));
        }

        return mappings;
    }

    private static string RequiredString(JsonObject obj, string key, string? location = null) =>
        OptionalString(obj[key], location ?? key) is { Length: > 0 } value ? value.Trim() : throw new PresetException($"{location ?? key}: is required");

    private static string? OptionalString(JsonNode? node, string location) => node switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        JsonValue v when v.GetValueKind() is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToJsonString(),
        _ => throw new PresetException($"{location}: must be a string"),
    };

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$")]
    private static partial Regex ParameterName();

    /// <summary><c>{{ NAME }}</c> or <c>{{ secret.NAME }}</c>, nothing else: other templates are left to the source and mapping.</summary>
    [GeneratedRegex(@"\{\{-?\s*(?<secret>secret\.)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*-?\}\}")]
    private static partial Regex Placeholder();
}
