using System.Text.Json.Nodes;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Http;

namespace Generral.Core.Sources.Types;

/// <summary>
/// Any JSON HTTP API, through the extraction engine. The settings are the extraction definition
/// (<c>requests</c>, <c>items</c>, <c>fields</c>); in the form, <c>requests</c> and <c>fields</c> may also be YAML text.
/// </summary>
public sealed class HttpJsonSourceType(SourceFetcherFactory fetchers, ExtractionEngine engine) : HttpExtractionSourceType(fetchers, engine)
{
    public override string Id => "http-json";

    public override string Name => "HTTP JSON";

    protected override DocumentFormat Format => DocumentFormat.Json;

    protected override IReadOnlyList<SourceField> TypeFields { get; } =
    [
        RequestsField,
        new()
        {
            Name = "items",
            Label = "Items",
            Value = "$[*]",
            HelpText = "JSONPath selecting the events in each response",
        },
        new()
        {
            Name = "fields",
            Label = "Fields",
            Type = SourceFieldType.TextArea,
            Value = new JsonObject
            {
                ["key"] = new JsonObject { ["path"] = "$.id" },
                ["title"] = new JsonObject { ["path"] = "$.name" },
            },
            HelpText = "Field name → extractor (path or text, filters). key and title are required",
        },
        DetailsField,
    ];
}
