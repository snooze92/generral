using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Generral.Core.Extensions;

/// <summary>Converts a YAML document to JSON nodes. Plain scalars become booleans, numbers or null where they look like one.</summary>
public static class YamlJson
{
    private const int MaxNodes = 100_000;
    private const int MaxDepth = 64;

    /// <exception cref="FormatException">Invalid YAML, or a document too large or too deep.</exception>
    public static JsonNode? Parse(string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException e)
        {
            throw new FormatException($"Invalid YAML at line {e.Start.Line}: {e.Message}", e);
        }
        catch (InvalidOperationException e)
        {
            // YamlDotNet's scanner reports some malformed flow collections this way.
            throw new FormatException($"Invalid YAML: {e.Message}", e);
        }

        if (stream.Documents.Count == 0)
        {
            return null;
        }

        var budget = MaxNodes;
        return Convert(stream.Documents[0].RootNode, 0, ref budget);
    }

    private static JsonNode? Convert(YamlNode node, int depth, ref int budget)
    {
        if (--budget < 0 || depth > MaxDepth)
        {
            throw new FormatException("YAML document is too large or too deeply nested");
        }

        switch (node)
        {
            case YamlMappingNode mapping:
                var obj = new JsonObject();
                foreach (var (key, value) in mapping.Children)
                {
                    var name = (key as YamlScalarNode)?.Value ?? throw new FormatException($"Unsupported mapping key at line {key.Start.Line}");
                    if (obj.ContainsKey(name))
                    {
                        throw new FormatException($"Duplicate key '{name}' at line {key.Start.Line}");
                    }

                    obj[name] = Convert(value, depth + 1, ref budget);
                }

                return obj;
            case YamlSequenceNode sequence:
                var array = new JsonArray();
                foreach (var child in sequence.Children)
                {
                    array.Add(Convert(child, depth + 1, ref budget));
                }

                return array;
            case YamlScalarNode scalar:
                return ConvertScalar(scalar);
            default:
                throw new FormatException($"Unsupported YAML node at line {node.Start.Line}");
        }
    }

    /// <summary>Writes JSON nodes as block YAML that <see cref="Parse"/> reads back to the same nodes.</summary>
    public static string ToYaml(JsonNode? node)
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        var emitter = new Emitter(writer);
        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart());
        Emit(emitter, node);
        emitter.Emit(new DocumentEnd(isImplicit: true));
        emitter.Emit(new StreamEnd());
        return writer.ToString();
    }

    private static void Emit(IEmitter emitter, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                emitter.Emit(new MappingStart(AnchorName.Empty, TagName.Empty, true, obj.Count == 0 ? MappingStyle.Flow : MappingStyle.Block));
                foreach (var (key, value) in obj)
                {
                    EmitString(emitter, key);
                    Emit(emitter, value);
                }

                emitter.Emit(new MappingEnd());
                break;
            case JsonArray array:
                emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, true, array.Count == 0 ? SequenceStyle.Flow : SequenceStyle.Block));
                foreach (var item in array)
                {
                    Emit(emitter, item);
                }

                emitter.Emit(new SequenceEnd());
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                EmitString(emitter, value.GetValue<string>());
                break;
            case JsonValue value:
                emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, value.ToJsonString(), ScalarStyle.Plain, true, false));
                break;
            default:
                emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, "null", ScalarStyle.Plain, true, false));
                break;
        }
    }

    /// <summary>Quoted when a plain scalar would read back as something else (number, boolean, null).</summary>
    private static void EmitString(IEmitter emitter, string value)
    {
        var plain = ConvertScalar(new YamlScalarNode(value) { Style = ScalarStyle.Plain });
        var style = plain is JsonValue v && v.GetValueKind() == JsonValueKind.String ? ScalarStyle.Any : ScalarStyle.DoubleQuoted;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, value, style, true, true));
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? "";
        if (scalar.Style != ScalarStyle.Plain)
        {
            return JsonValue.Create(value);
        }

        switch (value)
        {
            case "" or "~" or "null" or "Null" or "NULL":
                return null;
            case "true" or "True" or "TRUE":
                return JsonValue.Create(true);
            case "false" or "False" or "FALSE":
                return JsonValue.Create(false);
        }

        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(value);
    }
}
