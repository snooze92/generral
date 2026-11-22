using System.Collections;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Generral.Core.Templating;
using Json.Path;
using CssSelector = AngleSharp.Css.Dom.ISelector;

namespace Generral.Core.Sources.Extraction;

public sealed record ExtractionOptions
{
    /// <summary>Applied to dates without an offset.</summary>
    public TimeZoneInfo Timezone { get; init; } = TimeZoneInfo.Utc;

    /// <summary>Cap on fetched documents per extraction, all requests and pages included.</summary>
    public int MaxRequests { get; init; } = 50;

    public int MaxItems { get; init; } = 10_000;

    /// <summary>The raw item is kept under <c>_raw</c> up to this many characters of JSON.</summary>
    public int RawLimit { get; init; } = 16 * 1024;
}

/// <summary>
/// Runs an <see cref="ExtractionDefinition"/> over JSON or HTML documents: renders request URLs, follows pagination,
/// selects items and extracts fields. Template models: URLs see the <c>foreach</c> variables; field
/// <c>text</c> templates see the previous fields, plus <c>vars</c> (the <c>foreach</c> variables) and <c>request.url</c>.
/// </summary>
public sealed class ExtractionEngine(TemplateRenderer renderer, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <exception cref="ExtractionDefinitionException">Invalid path, template or filter.</exception>
    public CompiledExtraction Compile(ExtractionDefinition definition)
    {
        var names = definition.Fields.Select(f => f.Name).ToHashSet();
        foreach (var required in new[] { ReservedFields.Key, ReservedFields.Title })
        {
            if (!names.Contains(required))
            {
                throw new ExtractionDefinitionException($"fields.{required}: is required");
            }
        }

        if (names.Contains(ReservedFields.Raw))
        {
            throw new ExtractionDefinitionException($"fields.{ReservedFields.Raw}: is reserved");
        }

        var html = definition.Format == DocumentFormat.Html;
        if (!html && definition.HtmlPath is not null)
        {
            throw new ExtractionDefinitionException("htmlPath: only applies to HTML sources");
        }

        return new CompiledExtraction(
            definition.Format,
            definition.Requests.Select((r, i) => CompileRequest(r, $"requests[{i}]", html)).ToList(),
            html ? null : CompilePath(definition.Items, "items"),
            html ? HtmlExtraction.CompileSelector(definition.Items, "items") : null,
            definition.HtmlPath is { } htmlPath ? CompilePath(htmlPath, "htmlPath") : null,
            definition.Fields.Select(f => CompileField(f, html)).ToList());
    }

    public Task<ExtractionResult> ExtractAsync(ExtractionDefinition definition, IDocumentFetcher fetcher, ExtractionOptions? options = null, CancellationToken cancellationToken = default)
        => ExtractAsync(Compile(definition), fetcher, options, cancellationToken);

    /// <exception cref="ExtractionException">A URL or document is invalid, or the request cap is exceeded. Item problems are reported per item instead.</exception>
    public async Task<ExtractionResult> ExtractAsync(CompiledExtraction extraction, IDocumentFetcher fetcher, ExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ExtractionOptions();
        var run = new Run(options);
        foreach (var request in extraction.Requests)
        {
            foreach (var vars in ExpandForeach(request, cancellationToken))
            {
                var url = RenderUrl(request, vars, cancellationToken);
                await FetchPagesAsync(extraction, request, url, vars, fetcher, run, cancellationToken);
            }
        }

        return new ExtractionResult(run.Items, run.Urls, run.Warnings);
    }

    /// <summary>Extracts the items of an in-memory document (no requests, no pagination).</summary>
    /// <param name="url">Relative image URLs are resolved against it; null when the document has no URL.</param>
    public ExtractionResult Extract(CompiledExtraction extraction, JsonNode? document, Uri? url = null, ExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var run = new Run(options ?? new ExtractionOptions());
        AddItems(extraction, SelectItems(extraction.Items!, document), url, new Dictionary<string, object?>(), run, cancellationToken);
        return new ExtractionResult(run.Items, run.Urls, run.Warnings);
    }

    private async Task FetchPagesAsync(CompiledExtraction extraction, CompiledRequest request, Uri firstUrl, IReadOnlyDictionary<string, object?> vars, IDocumentFetcher fetcher, Run run, CancellationToken cancellationToken)
    {
        var pagination = request.Definition.Pagination;
        var page = pagination?.Start ?? 0;
        Uri? url = pagination?.PageParam is { } param ? WithQueryParameter(firstUrl, param, page.ToString(System.Globalization.CultureInfo.InvariantCulture)) : firstUrl;
        var seen = new HashSet<Uri>();
        for (var pageCount = 1; url is not null; pageCount++)
        {
            if (run.Urls.Count >= run.Options.MaxRequests)
            {
                if (pageCount == 1)
                {
                    throw new ExtractionException($"More than {run.Options.MaxRequests} requests");
                }

                run.Warnings.Add($"Stopped paginating {firstUrl}: request limit ({run.Options.MaxRequests}) reached");
                return;
            }

            seen.Add(url);
            run.Urls.Add(url);
            var document = await fetcher.FetchAsync(url, cancellationToken);
            var root = ParseDocument(extraction, document);
            var items = root is IDocument html ? HtmlExtraction.SelectItems(extraction.ItemSelector!, html) : SelectItems(extraction.Items!, (JsonNode?)root);
            if (!AddItems(extraction, items, document.Url, vars, run, cancellationToken) || pagination is null)
            {
                return;
            }

            Uri? next;
            if (pagination.PageParam is { } pageParam)
            {
                var more = items.Count > 0 || !pagination.UntilEmpty;
                next = more ? WithQueryParameter(firstUrl, pageParam, (++page).ToString(System.Globalization.CultureInfo.InvariantCulture)) : null;
            }
            else
            {
                next = NextUrl(request, root, document.Url);
                if (next is not null && seen.Contains(next))
                {
                    next = null;
                }
            }

            if (next is not null && pageCount >= pagination.MaxPages)
            {
                if (pagination.Next is not null || pagination.UntilEmpty)
                {
                    run.Warnings.Add($"Stopped paginating {firstUrl} after {pagination.MaxPages} pages (max_pages)");
                }

                return;
            }

            url = next;
        }
    }

    private bool AddItems(CompiledExtraction extraction, List<object?> items, Uri? url, IReadOnlyDictionary<string, object?> vars, Run run, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (run.Items.Count >= run.Options.MaxItems)
            {
                run.Warnings.Add($"Stopped at {run.Options.MaxItems} items");
                return false;
            }

            run.Items.Add(ExtractItem(extraction, item, run.Items.Count, url, vars, run, cancellationToken));
        }

        return true;
    }

    private ExtractedItem ExtractItem(CompiledExtraction extraction, object? item, int index, Uri? url, IReadOnlyDictionary<string, object?> vars, Run run, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object?>();
        var fields = new JsonObject();
        var errors = new List<string>();
        foreach (var field in extraction.Fields)
        {
            var definition = field.Definition;
            object? value = null;
            string? error = null;
            try
            {
                if (field.Path is not null)
                {
                    value = Select(field.Path, (JsonNode?)item);
                }
                else if (field.Selector is not null)
                {
                    value = HtmlExtraction.Select(field, (IElement)item!);
                }
                else if (field.Text is not null)
                {
                    value = RenderText(field.Text, values, vars, url, cancellationToken);
                }

                foreach (var filter in field.Filters)
                {
                    value = filter(value);
                }
            }
            catch (Exception e) when (e is FieldException or TemplateException or RegexMatchTimeoutException)
            {
                error = e.Message;
                value = null;
            }

            if (FieldValues.IsMissing(value))
            {
                value = definition.Default is not null ? FieldValues.FromJson(definition.Default) : null;
                if (value is null && !definition.Optional)
                {
                    errors.Add($"{definition.Name}: {error ?? "no value"}");
                }
            }

            values[definition.Name] = value;
            fields[definition.Name] = FieldValues.ToJson(value);
        }

        fields[ReservedFields.Raw] = Raw(item, run.Options.RawLimit);
        ExtractedEvent? extracted = null;
        if (errors.Count == 0)
        {
            extracted = ReservedFields.ToEvent(values, fields, run.Options.Timezone, url, errors);
            if (extracted is not null && !run.Keys.Add(extracted.Key))
            {
                errors.Add($"key: duplicate key '{extracted.Key}'");
                extracted = null;
            }
        }

        return new ExtractedItem(index, url, fields, extracted, errors);
    }

    private static object? RenderText(CompiledTemplate template, Dictionary<string, object?> values, IReadOnlyDictionary<string, object?> vars, Uri? url, CancellationToken cancellationToken)
    {
        var model = new Dictionary<string, object?>
        {
            ["vars"] = vars,
            ["request"] = new Dictionary<string, object?> { ["url"] = url?.AbsoluteUri },
        };
        foreach (var (name, value) in values)
        {
            model[name] = value;
        }

        return template.Render(model, cancellationToken);
    }

    private static object? Select(JsonPath path, JsonNode? item)
    {
        var matches = path.Evaluate(item).Matches;
        return matches.Count switch
        {
            0 => null,
            1 => FieldValues.FromJson(matches[0].Value),
            _ => new JsonArray(matches.Select(m => m.Value?.DeepClone()).ToArray()),
        };
    }

    private static List<object?> SelectItems(JsonPath path, JsonNode? root)
    {
        var matches = path.Evaluate(root).Matches;
        if (matches.Count == 1 && matches[0].Value is JsonArray array)
        {
            return [.. array];
        }

        return matches.Where(m => m.Value is not null).Select(m => (object?)m.Value).ToList();
    }

    private static Uri? NextUrl(CompiledRequest request, object? root, Uri current)
    {
        var text = (root is IDocument html ? HtmlExtraction.NextUrl(request.NextSelector!, html) : FieldValues.ToText(Select(request.Next!, (JsonNode?)root)))?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return Uri.TryCreate(current, text, out var next) && next.Scheme is "http" or "https"
            ? next
            : throw new ExtractionException($"Invalid next page URL '{text}'");
    }

    private static object? ParseDocument(CompiledExtraction extraction, FetchedDocument document)
    {
        if (extraction.Format == DocumentFormat.Html && extraction.HtmlPath is null)
        {
            return HtmlExtraction.Parse(document.Content);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(document.Content);
        }
        catch (JsonException e)
        {
            throw new ExtractionException($"{document.Url}: invalid JSON ({e.Message})", e);
        }

        if (extraction.HtmlPath is null)
        {
            return root;
        }

        return Select(extraction.HtmlPath, root) is string text
            ? HtmlExtraction.Parse(text)
            : throw new ExtractionException($"{document.Url}: htmlPath matched no string");
    }

    private static JsonNode? Raw(object? item, int limit)
    {
        if (item is IElement element)
        {
            var text = HtmlExtraction.RawText(element);
            return JsonValue.Create(text.Length <= limit ? text : text[..limit]);
        }

        if (item is not JsonNode node)
        {
            return null;
        }

        var json = node.ToJsonString();
        return json.Length <= limit ? node.DeepClone() : JsonValue.Create(json[..limit]);
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> ExpandForeach(CompiledRequest request, CancellationToken cancellationToken)
    {
        IEnumerable<Dictionary<string, object?>> combinations = [new Dictionary<string, object?>()];
        foreach (var (name, template) in request.Foreach)
        {
            object? evaluated;
            try
            {
                evaluated = template.Evaluate(new Dictionary<string, object?>(), cancellationToken);
            }
            catch (TemplateException e)
            {
                throw new ExtractionException($"foreach.{name}: {e.Message}", e);
            }

            var values = evaluated is IEnumerable enumerable and not string ? enumerable.Cast<object?>().ToList() : [evaluated];
            combinations = combinations.SelectMany(c => values.Select(v => new Dictionary<string, object?>(c) { [name] = v })).ToList();
        }

        return combinations;
    }

    private static Uri RenderUrl(CompiledRequest request, IReadOnlyDictionary<string, object?> vars, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = request.Url.Render(vars, cancellationToken).Trim();
        }
        catch (TemplateException e)
        {
            throw new ExtractionException($"url: {e.Message}", e);
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Scheme is "http" or "https"
            ? url
            : throw new ExtractionException($"Invalid request URL '{text}'");
    }

    private static Uri WithQueryParameter(Uri url, string name, string value)
    {
        var encodedName = WebUtility.UrlEncode(name);
        var pairs = url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Split('=', 2)[0] != encodedName)
            .Append($"{encodedName}={WebUtility.UrlEncode(value)}");
        return new UriBuilder(url) { Query = string.Join('&', pairs) }.Uri;
    }

    private CompiledRequest CompileRequest(RequestDefinition request, string location, bool html)
    {
        var next = request.Pagination?.Next;
        return new CompiledRequest(
            request,
            CompileTemplate(request.Url, $"{location}.url"),
            request.Foreach.Select(v => KeyValuePair.Create(v.Key, CompileTemplate(v.Value, $"{location}.foreach.{v.Key}"))).ToList(),
            next is not null && !html ? CompilePath(next, $"{location}.pagination.next") : null,
            next is not null && html ? HtmlExtraction.CompileSelector(next, $"{location}.pagination.next") : null);
    }

    private CompiledField CompileField(FieldDefinition field, bool html)
    {
        var location = $"fields.{field.Name}";
        if (html ? field.Path is not null : field.Selector is not null)
        {
            throw new ExtractionDefinitionException(html ? $"{location}.path: HTML sources use 'selector'" : $"{location}.selector: JSON sources use 'path'");
        }

        var filters = field.Filters.Select((f, i) =>
        {
            try
            {
                return FieldFilters.Create(f.Name, f.Args, _time);
            }
            catch (ArgumentException e)
            {
                throw new ExtractionDefinitionException($"{location}.filters[{i}] ({f.Name}): {e.Message}");
            }
        }).ToList();

        return new CompiledField(
            field,
            field.Path is not null ? CompilePath(field.Path, $"{location}.path") : null,
            field.Selector is not null ? HtmlExtraction.CompileSelector(field.Selector, $"{location}.selector") : null,
            field.Remove is not null ? HtmlExtraction.CompileSelector(field.Remove, $"{location}.remove") : null,
            field.Text is not null ? CompileTemplate(field.Text, $"{location}.text") : null,
            filters);
    }

    private CompiledTemplate CompileTemplate(string text, string location)
    {
        try
        {
            return renderer.Compile(text);
        }
        catch (TemplateException e)
        {
            throw new ExtractionDefinitionException($"{location}: {e.Message}");
        }
    }

    private static JsonPath CompilePath(string path, string location) =>
        JsonPath.TryParse(path, out var result) ? result : throw new ExtractionDefinitionException($"{location}: invalid JSONPath '{path}'");

    private sealed class Run(ExtractionOptions options)
    {
        public ExtractionOptions Options { get; } = options;

        public List<ExtractedItem> Items { get; } = [];

        public List<Uri> Urls { get; } = [];

        public List<string> Warnings { get; } = [];

        public HashSet<string> Keys { get; } = [];
    }
}

public sealed class CompiledExtraction
{
    internal CompiledExtraction(DocumentFormat format, IReadOnlyList<CompiledRequest> requests, JsonPath? items, CssSelector? itemSelector, JsonPath? htmlPath, IReadOnlyList<CompiledField> fields)
    {
        Format = format;
        Requests = requests;
        Items = items;
        ItemSelector = itemSelector;
        HtmlPath = htmlPath;
        Fields = fields;
    }

    internal DocumentFormat Format { get; }

    internal IReadOnlyList<CompiledRequest> Requests { get; }

    /// <summary>JSON items.</summary>
    internal JsonPath? Items { get; }

    /// <summary>HTML items.</summary>
    internal CssSelector? ItemSelector { get; }

    internal JsonPath? HtmlPath { get; }

    internal IReadOnlyList<CompiledField> Fields { get; }
}

internal sealed record CompiledRequest(RequestDefinition Definition, CompiledTemplate Url, IReadOnlyList<KeyValuePair<string, CompiledTemplate>> Foreach, JsonPath? Next, CssSelector? NextSelector);

internal sealed record CompiledField(FieldDefinition Definition, JsonPath? Path, CssSelector? Selector, CssSelector? Remove, CompiledTemplate? Text, IReadOnlyList<FieldFilter> Filters);
