using System.Text.Json;
using System.Text.Json.Nodes;
using Generral.Core.Extensions;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Http;

namespace Generral.Core.Sources.Types;

/// <summary>The extraction-engine types: the settings are the extraction definition, <c>requests</c> and <c>fields</c> may be YAML text.</summary>
public abstract class HttpExtractionSourceType(SourceFetcherFactory fetchers, ExtractionEngine engine) : SourceType<CompiledExtraction?>(fetchers)
{
    protected static readonly SourceField RequestsField = new()
    {
        Name = "requests",
        Label = "Requests",
        Type = SourceFieldType.TextArea,
        Value = new JsonArray("https://api.example.org/events"),
        HelpText = "URLs (Scriban templates), optionally with foreach variables and pagination",
    };

    protected abstract DocumentFormat Format { get; }

    protected override CompiledExtraction? Read(SourceSettings settings)
    {
        if (!TryStructured(settings, "requests", out var requests) | !TryStructured(settings, "fields", out var fields))
        {
            return null;
        }

        try
        {
            var definition = new JsonObject
            {
                ["requests"] = requests is JsonValue url ? new JsonArray(url) : requests,
                ["items"] = settings["items"]?.DeepClone(),
                ["fields"] = fields,
            };
            return engine.Compile(ExtractionDefinitionParser.FromJson(definition) with
            {
                Format = Format,
                HtmlPath = Format == DocumentFormat.Html ? settings.String("htmlPath") : null,
            });
        }
        catch (ExtractionDefinitionException e)
        {
            var property = e.Message.Split([':', '.', '['], 2)[0];
            settings.Fail(property is "requests" or "items" or "fields" or "htmlPath" ? property : "definition", e.Message);
            return null;
        }
    }

    protected override async Task<SourceFetchResult> FetchAsync(CompiledExtraction? config, SourceFetchContext context, CancellationToken cancellationToken)
    {
        var result = await engine.ExtractAsync(config!, context.Fetcher, context.ExtractionOptions, cancellationToken);
        return Result(context, result);
    }

    /// <summary>A string holding a YAML (or JSON) list or mapping is parsed; anything else, e.g. a single URL, is used as is.</summary>
    private static bool TryStructured(SourceSettings settings, string name, out JsonNode? result)
    {
        result = settings[name]?.DeepClone();
        if (result is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
        {
            return true;
        }

        try
        {
            if (YamlJson.Parse(value.GetValue<string>()) is { } parsed and (JsonArray or JsonObject))
            {
                result = parsed;
            }

            return true;
        }
        catch (FormatException e)
        {
            settings.Fail(name, e.Message);
            return false;
        }
    }
}
