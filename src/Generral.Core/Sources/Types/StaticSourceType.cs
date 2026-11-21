using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Generral.Core.Extensions;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Http;
using Json.Path;

namespace Generral.Core.Sources.Types;

/// <summary>
/// Events typed or pasted by the user, as YAML, JSON or CSV (header row). Without <c>fields</c>, each
/// column or property is a field as is, so the reserved names (<c>key</c>, <c>title</c>, <c>start</c>, …) apply directly.
/// </summary>
public sealed class StaticSourceType(SourceFetcherFactory fetchers, ExtractionEngine engine) : SourceType<StaticSourceType.Config?>(fetchers)
{
    public const int MaxLength = 1024 * 1024;

    public override string Id => "static";

    public override string Name => "Static";

    protected override bool UsesHttp => false;

    protected override IReadOnlyList<SourceField> TypeFields { get; } =
    [
        new()
        {
            Name = "format",
            Label = "Format",
            Type = SourceFieldType.Select,
            Value = "yaml",
            SelectOptions = [new("yaml", "YAML"), new("json", "JSON"), new("csv", "CSV")],
        },
        new()
        {
            Name = "events",
            Label = "Events",
            Type = SourceFieldType.TextArea,
            Value = "",
            Placeholder = "- key: final\n  title: Final\n  start: 2027-11-13T15:00:00Z",
            HelpText = "A list of events. key and title are required; start, end, status and image URLs are optional, other properties are kept as fields",
        },
        new()
        {
            Name = "items",
            Label = "Items",
            HelpText = "JSONPath selecting the events (YAML/JSON only). Defaults to the root list, or the 'events' list of the root object",
            Advanced = true,
        },
        new()
        {
            Name = "fields",
            Label = "Fields",
            Type = SourceFieldType.TextArea,
            HelpText = "Optional field extractors (as for HTTP JSON sources), to rename or transform properties",
            Advanced = true,
        },
    ];

    protected override Config? Read(SourceSettings settings)
    {
        var format = settings.String("format") ?? "yaml";
        var text = settings.String("events") ?? "";
        if (text.Length > MaxLength)
        {
            settings.Fail("events", $"must be at most {MaxLength / 1024} KiB");
            return null;
        }

        JsonNode? document;
        try
        {
            document = format switch
            {
                "yaml" => YamlJson.Parse(text),
                "json" => text.Length == 0 ? null : JsonNode.Parse(text),
                "csv" => Csv.Parse(text),
                _ => throw new FormatException("format: must be yaml, json or csv"),
            };
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            settings.Fail(e.Message.StartsWith("format:", StringComparison.Ordinal) ? "format" : "events", e.Message);
            return null;
        }

        document ??= new JsonArray();
        var items = settings.String("items") ?? (document is JsonObject { } root && root.ContainsKey("events") ? "$.events[*]" : "$[*]");
        try
        {
            var fields = settings["fields"] is { } node
                ? ExtractionDefinitionParser.FieldsFromJson(node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? YamlJson.Parse(value.GetValue<string>()) : node)
                : null;
            var definition = new ExtractionDefinition { Requests = [], Items = items, Fields = fields ?? PassThroughFields(items, document) };
            return new Config(engine.Compile(definition), document);
        }
        catch (Exception e) when (e is ExtractionDefinitionException or FormatException)
        {
            settings.Fail(e.Message.StartsWith("items", StringComparison.Ordinal) ? "items" : "fields", e.Message);
            return null;
        }
    }

    protected override Task<SourceFetchResult> FetchAsync(Config? config, SourceFetchContext context, CancellationToken cancellationToken)
    {
        var result = engine.Extract(config!.Extraction, config.Document, null, context.ExtractionOptions, cancellationToken);
        return Task.FromResult(Result(context, result));
    }

    /// <summary>One field per property found in the items, in order of appearance; only key and title are required.</summary>
    private static List<FieldDefinition> PassThroughFields(string items, JsonNode document)
    {
        if (!JsonPath.TryParse(items, out var path))
        {
            throw new ExtractionDefinitionException($"items: invalid JSONPath '{items}'");
        }

        var matches = path.Evaluate(document).Matches;
        var selected = matches is [{ Value: JsonArray array }] ? array : matches.Select(m => m.Value);
        var names = new List<string> { ReservedFields.Key, ReservedFields.Title };
        foreach (var item in selected.OfType<JsonObject>())
        {
            names.AddRange(item.Select(p => p.Key).Where(n => n.Length > 0 && n != ReservedFields.Raw && !names.Contains(n)));
        }

        return names.Select(n => new FieldDefinition
        {
            Name = n,
            Path = $"$['{n.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}']",
            Optional = n is not (ReservedFields.Key or ReservedFields.Title),
        }).ToList();
    }

    public sealed record Config(CompiledExtraction Extraction, JsonNode Document);

    /// <summary>RFC 4180 CSV with a header row, to a list of objects. Empty cells are omitted.</summary>
    internal static class Csv
    {
        /// <exception cref="FormatException">Unterminated quotes, or a row with more cells than the header.</exception>
        public static JsonArray Parse(string text)
        {
            var rows = Rows(text).Where(r => r.Count > 1 || r[0].Length > 0).ToList();
            var result = new JsonArray();
            if (rows.Count == 0)
            {
                return result;
            }

            var header = rows[0].Select(h => h.Trim()).ToList();
            for (var i = 1; i < rows.Count; i++)
            {
                if (rows[i].Count > header.Count)
                {
                    throw new FormatException($"CSV row {i + 1} has {rows[i].Count} cells, the header has {header.Count}");
                }

                var item = new JsonObject();
                for (var c = 0; c < rows[i].Count; c++)
                {
                    if (rows[i][c].Length > 0 && header[c].Length > 0)
                    {
                        item[header[c]] = rows[i][c];
                    }
                }

                result.Add(item);
            }

            return result;
        }

        private static IEnumerable<List<string>> Rows(string text)
        {
            var row = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c != '"')
                    {
                        cell.Append(c);
                    }
                    else if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else if (c == '"' && cell.Length == 0)
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                }
                else if (c is '\n' or '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    row.Add(cell.ToString());
                    cell.Clear();
                    yield return row;
                    row = [];
                }
                else
                {
                    cell.Append(c);
                }
            }

            if (quoted)
            {
                throw new FormatException("CSV: unterminated quoted cell");
            }

            row.Add(cell.ToString());
            yield return row;
        }
    }
}
