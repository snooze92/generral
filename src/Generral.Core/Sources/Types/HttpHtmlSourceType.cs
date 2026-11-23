using System.Text.Json.Nodes;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Http;

namespace Generral.Core.Sources.Types;

/// <summary>
/// An HTML page (e.g. a Wikipedia table), through the extraction engine: <c>items</c> and the fields' <c>selector</c>
/// are CSS selectors. <c>htmlPath</c> reads the HTML from a JSON response instead (e.g. MediaWiki's parse API).
/// </summary>
public sealed class HttpHtmlSourceType(SourceFetcherFactory fetchers, ExtractionEngine engine) : HttpExtractionSourceType(fetchers, engine)
{
    public override string Id => "http-html";

    public override string Name => "HTTP HTML";

    protected override DocumentFormat Format => DocumentFormat.Html;

    protected override IReadOnlyList<SourceField> TypeFields { get; } =
    [
        RequestsField,
        new()
        {
            Name = "htmlPath",
            Label = "HTML Path",
            HelpText = "Optional: JSONPath to the HTML string when the response is JSON (e.g. $.parse.text for the MediaWiki API)",
            Advanced = true,
        },
        new()
        {
            Name = "items",
            Label = "Items",
            Value = "table tr:has(td)",
            HelpText = "CSS selector of the events in each page (:has() is supported)",
        },
        new()
        {
            Name = "fields",
            Label = "Fields",
            Type = SourceFieldType.TextArea,
            Value = new JsonObject
            {
                ["title"] = new JsonObject { ["selector"] = "td:nth-child(1)" },
                ["key"] = new JsonObject { ["text"] = "{{ title | string.slugify }}" },
            },
            HelpText = "Field name → extractor (selector with optional attribute/remove, or text; filters). key and title are required",
        },
        DetailsField,
    ];
}
