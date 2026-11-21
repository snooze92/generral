using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Sources.Extraction;

namespace Generral.Core.Sources;

/// <summary>Content hash of an extracted event: everything but its key, so that any change creates a revision.</summary>
public static class EventHash
{
    public static string Compute(ExtractedEvent extracted)
    {
        var content = new JsonObject
        {
            ["title"] = extracted.Title,
            ["start"] = Format(extracted.Start),
            ["end"] = Format(extracted.End),
            ["timezone"] = extracted.Timezone,
            ["status"] = extracted.Status.ToString(),
            ["fields"] = extracted.Fields.DeepClone(),
            ["images"] = JsonSerializer.SerializeToNode(extracted.Images, JsonColumn.Options),
        };

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToJsonString())));
    }

    private static string? Format(DateTime? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
