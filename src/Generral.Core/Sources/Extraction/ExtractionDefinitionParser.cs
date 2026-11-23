using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Generral.Core.Extensions;

namespace Generral.Core.Sources.Extraction;

/// <summary>
/// Reads an <see cref="ExtractionDefinition"/> from YAML or JSON. Top-level keys other than
/// <c>requests</c>, <c>items</c>, <c>fields</c> and <c>details</c> belong to the source type and are ignored; nested objects are strict.
/// </summary>
public static class ExtractionDefinitionParser
{
    private static readonly string[] RequestKeys = ["url", "foreach", "pagination"];
    private static readonly string[] PaginationKeys = ["next", "page_param", "start", "until_empty", "max_pages"];
    private static readonly string[] FieldKeys = ["path", "selector", "attribute", "remove", "text", "filters", "optional", "default"];
    private static readonly string[] FilterKeys = ["name", "args"];
    private static readonly string[] DetailsKeys = ["url", "format", "htmlPath", "fields", "optional", "cache"];

    /// <exception cref="ExtractionDefinitionException">Invalid YAML or definition.</exception>
    public static ExtractionDefinition FromYaml(string yaml)
    {
        JsonNode? node;
        try
        {
            node = YamlJson.Parse(yaml);
        }
        catch (FormatException e)
        {
            throw new ExtractionDefinitionException(e.Message);
        }

        return FromJson(node);
    }

    /// <exception cref="ExtractionDefinitionException">Invalid JSON or definition.</exception>
    public static ExtractionDefinition FromJson(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException e)
        {
            throw new ExtractionDefinitionException($"Invalid JSON: {e.Message}");
        }

        return FromJson(node);
    }

    public static ExtractionDefinition FromJson(JsonNode? node)
    {
        var root = AsObject(node, "definition");
        var requests = root["requests"] switch
        {
            JsonArray array when array.Count > 0 => array.Select((r, i) => ParseRequest(r, $"requests[{i}]")).ToList(),
            JsonObject single => [ParseRequest(single, "requests")],
            _ => throw Error("requests", "at least one request is required"),
        };

        return new ExtractionDefinition
        {
            Requests = requests,
            Items = RequiredString(root, "items", "items"),
            Fields = FieldsFromJson(root["fields"] ?? throw Error("fields", "is required")),
            Details = root["details"] is { } details ? ParseDetails(details) : null,
        };
    }

    /// <summary>Reads the <c>fields</c> mapping (field name → extractor) on its own.</summary>
    public static IReadOnlyList<FieldDefinition> FieldsFromJson(JsonNode? node) => ParseFields(node, "fields");

    private static IReadOnlyList<FieldDefinition> ParseFields(JsonNode? node, string location)
    {
        var fields = AsObject(node, location)
            .Select(f => ParseField(f.Key, f.Value, location))
            .ToList();
        if (fields.Count == 0)
        {
            throw Error(location, "at least one field is required");
        }

        return fields;
    }

    private static DetailsDefinition ParseDetails(JsonNode node)
    {
        const string location = "details";
        var obj = Strict(node, location, DetailsKeys);
        var details = new DetailsDefinition
        {
            Url = RequiredString(obj, "url", location),
            Format = String(obj["format"], $"{location}.format") switch
            {
                null => null,
                "json" => DocumentFormat.Json,
                "html" => DocumentFormat.Html,
                _ => throw Error($"{location}.format", "must be 'json' or 'html'"),
            },
            HtmlPath = String(obj["htmlPath"], $"{location}.htmlPath"),
            Fields = ParseFields(obj["fields"] ?? throw Error($"{location}.fields", "is required"), $"{location}.fields"),
            Optional = Bool(obj["optional"], $"{location}.optional") ?? false,
            Cache = obj["cache"] is { } cache ? Duration(cache, $"{location}.cache") : DetailsDefinition.DefaultCache,
        };

        if (details.Cache > DetailsDefinition.MaxCache)
        {
            throw Error($"{location}.cache", "must be at most 30d");
        }

        return details;
    }

    /// <summary><c>0</c>, or a number with a unit: <c>30m</c>, <c>12h</c>, <c>7d</c>.</summary>
    private static TimeSpan Duration(JsonNode node, string location)
    {
        var text = Scalar(node, location).Trim();
        if (text == "0")
        {
            return TimeSpan.Zero;
        }

        if (text.Length >= 2 && int.TryParse(text[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            switch (text[^1])
            {
                case 'm': return TimeSpan.FromMinutes(amount);
                case 'h': return TimeSpan.FromHours(amount);
                case 'd': return TimeSpan.FromDays(amount);
            }
        }

        throw Error(location, "must be 0 or a duration such as 30m, 12h or 7d");
    }

    private static RequestDefinition ParseRequest(JsonNode? node, string location)
    {
        if (node is JsonValue)
        {
            return new RequestDefinition { Url = String(node, location)! };
        }

        var obj = Strict(node, location, RequestKeys);
        var foreachVars = obj["foreach"] is { } f
            ? AsObject(f, $"{location}.foreach").Select(v => KeyValuePair.Create(v.Key, String(v.Value, $"{location}.foreach.{v.Key}") ?? "")).ToList()
            : [];

        return new RequestDefinition
        {
            Url = RequiredString(obj, "url", location),
            Foreach = foreachVars,
            Pagination = obj["pagination"] is { } p ? ParsePagination(p, $"{location}.pagination") : null,
        };
    }

    private static PaginationDefinition ParsePagination(JsonNode node, string location)
    {
        var obj = Strict(node, location, PaginationKeys);
        var pagination = new PaginationDefinition
        {
            Next = String(obj["next"], $"{location}.next"),
            PageParam = String(obj["page_param"], $"{location}.page_param"),
            Start = Int(obj["start"], $"{location}.start") ?? 1,
            UntilEmpty = Bool(obj["until_empty"], $"{location}.until_empty") ?? true,
            MaxPages = Int(obj["max_pages"], $"{location}.max_pages") ?? PaginationDefinition.DefaultMaxPages,
        };

        if ((pagination.Next is null) == (pagination.PageParam is null))
        {
            throw Error(location, "set either 'next' or 'page_param'");
        }

        if (pagination.MaxPages is < 1 or > PaginationDefinition.MaxPagesLimit)
        {
            throw Error($"{location}.max_pages", $"must be between 1 and {PaginationDefinition.MaxPagesLimit}");
        }

        return pagination;
    }

    private static FieldDefinition ParseField(string name, JsonNode? node, string parent)
    {
        var location = $"{parent}.{name}";
        if (name.Length == 0)
        {
            throw Error(parent, "field names must not be empty");
        }

        var obj = Strict(node, location, FieldKeys);
        var field = new FieldDefinition
        {
            Name = name,
            Path = String(obj["path"], $"{location}.path"),
            Selector = String(obj["selector"], $"{location}.selector"),
            Attribute = String(obj["attribute"], $"{location}.attribute"),
            Remove = String(obj["remove"], $"{location}.remove"),
            Text = String(obj["text"], $"{location}.text"),
            Filters = obj["filters"] switch
            {
                null => [],
                JsonArray filters => filters.Select((f, i) => ParseFilter(f, $"{location}.filters[{i}]")).ToList(),
                _ => throw Error($"{location}.filters", "must be a list"),
            },
            Optional = Bool(obj["optional"], $"{location}.optional") ?? false,
            Default = obj["default"]?.DeepClone(),
        };

        var sources = new[] { field.Path, field.Selector, field.Text }.Count(s => s is not null);
        if (sources > 1)
        {
            throw Error(location, "set only one of 'path', 'selector' and 'text'");
        }

        if (sources == 0 && field.Default is null)
        {
            throw Error(location, "set 'path', 'selector', 'text' or 'default'");
        }

        if (field.Selector is null && (field.Attribute is not null || field.Remove is not null))
        {
            throw Error(location, "'attribute' and 'remove' need a 'selector'");
        }

        return field;
    }

    private static FilterDefinition ParseFilter(JsonNode? node, string location)
    {
        if (node is JsonValue)
        {
            return new FilterDefinition(String(node, location)!, []);
        }

        var obj = Strict(node, location, FilterKeys);
        var args = obj["args"] switch
        {
            null => [],
            JsonArray array => array.Select((a, i) => Scalar(a, $"{location}.args[{i}]")).ToList(),
            var single => (IReadOnlyList<string>)[Scalar(single, $"{location}.args")],
        };

        return new FilterDefinition(RequiredString(obj, "name", location), args);
    }

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

    private static string RequiredString(JsonObject obj, string key, string location) =>
        String(obj[key], $"{location}.{key}") is { Length: > 0 } value ? value : throw Error($"{location}.{key}", "is required");

    private static string? String(JsonNode? node, string location) => node switch
    {
        null => null,
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        _ => throw Error(location, "must be a string"),
    };

    private static string Scalar(JsonNode? node, string location) => node switch
    {
        null => "",
        JsonValue value => value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : FieldValues.ToText(FieldValues.FromJson(value)) ?? "",
        _ => throw Error(location, "must be a string or a number"),
    };

    private static int? Int(JsonNode? node, string location) => node switch
    {
        null => null,
        JsonValue value when FieldValues.FromJson(value) is long l and >= int.MinValue and <= int.MaxValue => (int)l,
        JsonValue value when value.GetValueKind() == JsonValueKind.String
            && int.TryParse(value.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        _ => throw Error(location, "must be an integer"),
    };

    private static bool? Bool(JsonNode? node, string location) => node switch
    {
        null => null,
        JsonValue value when value.GetValueKind() is JsonValueKind.True or JsonValueKind.False => value.GetValue<bool>(),
        _ => throw Error(location, "must be true or false"),
    };

    private static ExtractionDefinitionException Error(string location, string message) => new($"{location}: {message}");
}
