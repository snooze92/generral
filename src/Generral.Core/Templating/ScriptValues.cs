using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Scriban.Runtime;

namespace Generral.Core.Templating;

/// <summary>Converts model values (dictionaries, lists, JSON nodes) into Scriban objects, keeping member names as-is.</summary>
public static class ScriptValues
{
    public static ScriptObject ToScriptObject(IEnumerable<KeyValuePair<string, object?>> values)
    {
        var result = new ScriptObject();
        foreach (var (key, value) in values)
        {
            result.SetValue(key, ToScriptValue(value), false);
        }

        return result;
    }

    public static object? ToScriptValue(object? value) => value switch
    {
        null => null,
        string or bool or int or long or double or decimal or DateTime or DateTimeOffset or TimeSpan => value,
        ScriptObject or ScriptArray or ScriptRange => value,
        JsonNode node => FromJson(node),
        IEnumerable<KeyValuePair<string, object?>> dictionary => ToScriptObject(dictionary),
        IDictionary dictionary => ToScriptObject(dictionary.Keys.Cast<object>()
            .Select(k => new KeyValuePair<string, object?>(Convert.ToString(k, CultureInfo.InvariantCulture)!, dictionary[k]))),
        IEnumerable list => new ScriptArray(list.Cast<object?>().Select(ToScriptValue)),
        _ => value,
    };

    private static object? FromJson(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                var result = new ScriptObject();
                foreach (var (key, child) in obj)
                {
                    result.SetValue(key, child is null ? null : FromJson(child), false);
                }

                return result;
            case JsonArray array:
                return new ScriptArray(array.Select(child => child is null ? null : FromJson(child)));
            default:
                if (!node.AsValue().TryGetValue<JsonElement>(out var element))
                {
                    element = JsonSerializer.SerializeToElement(node);
                }

                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number when element.TryGetInt64(out var l) => l is >= int.MinValue and <= int.MaxValue ? (int)l : l,
                    JsonValueKind.Number => element.GetDouble(),
                    _ => null,
                };
        }
    }
}
