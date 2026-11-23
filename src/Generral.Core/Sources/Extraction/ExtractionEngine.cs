using System.Collections;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>Cap on details requests per extraction (cached details don't count). Items over it get no details.</summary>
    public int MaxDetailsRequests { get; init; } = 200;

    /// <summary>Details requests in flight at once (the fetcher still spaces them by the request delay).</summary>
    public int DetailsConcurrency { get; init; } = 3;
}

/// <summary>
/// Runs an <see cref="ExtractionDefinition"/> over JSON or HTML documents: renders request URLs, follows pagination,
/// selects items and extracts fields. Template models: URLs see the <c>foreach</c> variables; field
/// <c>text</c> templates see the previous fields, plus <c>vars</c> (the <c>foreach</c> variables) and <c>request.url</c>.
/// </summary>
/// <remarks>An optional <c>details</c> request per item (URL templated from the item's fields) adds or overrides fields; see § Details requests.</remarks>
public sealed class ExtractionEngine(TemplateRenderer renderer, TimeProvider? time = null, DetailsCache? detailsCache = null)
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
            definition.Fields.Select(f => CompileField(f, html, "fields")).ToList(),
            definition.Details is { } details ? CompileDetails(details, definition.Format) : null);
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

        if (extraction.Details is { } details)
        {
            await AddDetailsAsync(details, fetcher, run, cancellationToken);
        }

        return Finish(run);
    }

    /// <summary>Extracts the items of an in-memory document (no requests, no pagination).</summary>
    /// <param name="url">Relative image URLs are resolved against it; null when the document has no URL.</param>
    public ExtractionResult Extract(CompiledExtraction extraction, JsonNode? document, Uri? url = null, ExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var run = new Run(options ?? new ExtractionOptions());
        AddItems(extraction, SelectItems(extraction.Items!, document), url, new Dictionary<string, object?>(), run, cancellationToken);
        return Finish(run);
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
            var root = ParseDocument(extraction.Format, extraction.HtmlPath, document);
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

    private PendingItem ExtractItem(CompiledExtraction extraction, object? item, int index, Uri? url, IReadOnlyDictionary<string, object?> vars, Run run, CancellationToken cancellationToken)
    {
        var pending = new PendingItem(index, url, vars);
        EvaluateFields(extraction.Fields, pending, pending.Values, pending.Fields, pending.Errors, url, item, cancellationToken);
        pending.Fields[ReservedFields.Raw] = Raw(item, run.Options.RawLimit);
        return pending;
    }

    /// <summary>Evaluates <paramref name="fields"/> in order against <paramref name="node"/> (a JSON node or an HTML element), into <paramref name="values"/>.</summary>
    private static void EvaluateFields(
        IReadOnlyList<CompiledField> fields, PendingItem item, Dictionary<string, object?> values, JsonObject json, List<string> errors, Uri? url, object? node, CancellationToken cancellationToken)
    {
        foreach (var field in fields)
        {
            var definition = field.Definition;
            object? value = null;
            string? error = null;
            try
            {
                if (field.Path is not null)
                {
                    value = Select(field.Path, (JsonNode?)node);
                }
                else if (field.Selector is not null)
                {
                    value = HtmlExtraction.Select(field, (IElement)node!);
                }
                else if (field.Text is not null)
                {
                    value = RenderText(field.Text, item.Values, ReferenceEquals(values, item.Values) ? null : values, item.Vars, url, cancellationToken);
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
            json[definition.Name] = FieldValues.ToJson(value);
        }
    }

    private static ExtractionResult Finish(Run run)
    {
        var items = new List<ExtractedItem>(run.Items.Count);
        foreach (var pending in run.Items)
        {
            ExtractedEvent? extracted = null;
            if (pending.Errors.Count == 0)
            {
                extracted = ReservedFields.ToEvent(pending.Values, pending.Fields, run.Options.Timezone, pending.Url, pending.Errors);
                if (extracted is not null && !run.Keys.Add(extracted.Key))
                {
                    pending.Errors.Add($"key: duplicate key '{extracted.Key}'");
                    extracted = null;
                }
            }

            items.Add(new ExtractedItem(pending.Index, pending.Url, pending.Fields, extracted, pending.Errors));
        }

        return new ExtractionResult(items, run.Urls, run.Warnings);
    }

    /// <summary>
    /// One details request per item without errors, at most <see cref="ExtractionOptions.DetailsConcurrency"/> at a time. Details of an
    /// unchanged item (same fields and URL) come from the <see cref="DetailsCache"/>. Failures are item errors (or ignored when optional).
    /// </summary>
    private async Task AddDetailsAsync(CompiledDetails details, IDocumentFetcher fetcher, Run run, CancellationToken cancellationToken)
    {
        var requests = 0;
        var skipped = 0;
        var optionalFailures = new List<string>();
        using var slots = new SemaphoreSlim(Math.Max(1, run.Options.DetailsConcurrency));
        var tasks = run.Items.Where(i => i.Errors.Count == 0).Select(async item =>
        {
            string? text;
            try
            {
                text = RenderText(details.Url, item.Values, null, item.Vars, item.Url, cancellationToken)?.ToString()?.Trim();
            }
            catch (TemplateException e)
            {
                Fail(item, $"url: {e.Message}");
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (!(item.Url is null ? Uri.TryCreate(text, UriKind.Absolute, out var url) : Uri.TryCreate(item.Url, text, out url)) || url.Scheme is not ("http" or "https"))
            {
                Fail(item, $"invalid URL '{text}'");
                return;
            }

            var cacheKey = details.CacheKey(url, item.Fields);
            if (details.Definition.Cache > TimeSpan.Zero && detailsCache?.TryGet(cacheKey) is { } cached)
            {
                Merge(item, cached.Select(f => KeyValuePair.Create(f.Key, FieldValues.FromJson(f.Value))));
                return;
            }

            if (Interlocked.Increment(ref requests) > run.Options.MaxDetailsRequests)
            {
                Interlocked.Increment(ref skipped);
                return;
            }

            await slots.WaitAsync(cancellationToken);
            object? root;
            try
            {
                var document = await fetcher.FetchAsync(url, cancellationToken);
                root = ParseDocument(details.Format, details.HtmlPath, document);
            }
            catch (Exception e) when (e is HttpRequestException or TimeoutException or ExtractionException)
            {
                Fail(item, e.Message);
                return;
            }
            finally
            {
                slots.Release();
            }

            var values = new Dictionary<string, object?>();
            var json = new JsonObject();
            var errors = new List<string>();
            EvaluateFields(details.Fields, item, values, json, errors, url, root is IDocument html ? html.DocumentElement : root, cancellationToken);
            if (errors.Count > 0)
            {
                foreach (var error in errors)
                {
                    Fail(item, $"fields.{error}");
                }

                return;
            }

            foreach (var name in values.Keys.Where(ReservedFields.ImageFields.Contains).ToList())
            {
                // Relative image URLs belong to the details page, not to the list page the item came from.
                if (FieldValues.ToText(values[name])?.Trim() is { Length: > 0 } image && Uri.TryCreate(url, image, out var absolute))
                {
                    values[name] = absolute.AbsoluteUri;
                    json[name] = absolute.AbsoluteUri;
                }
            }

            if (details.Definition.Cache > TimeSpan.Zero)
            {
                detailsCache?.Set(cacheKey, json, details.Definition.Cache);
            }

            Merge(item, values);
        }).ToList();

        await Task.WhenAll(tasks);

        if (skipped > 0)
        {
            run.Warnings.Add($"{skipped} item(s) got no details: details request limit ({run.Options.MaxDetailsRequests}) reached");
        }

        if (optionalFailures.Count > 0)
        {
            run.Warnings.Add($"Details failed for {optionalFailures.Count} item(s) (optional), e.g. {optionalFailures[0]}");
        }

        void Fail(PendingItem item, string error)
        {
            if (details.Definition.Optional)
            {
                lock (optionalFailures)
                {
                    optionalFailures.Add(error);
                }

                return;
            }

            item.Errors.Add($"details: {error}");
        }
    }

    private static void Merge(PendingItem item, IEnumerable<KeyValuePair<string, object?>> values)
    {
        foreach (var (name, value) in values)
        {
            item.Values[name] = value;
            item.Fields[name] = FieldValues.ToJson(value);
        }
    }

    /// <param name="values">The item's fields, by name and as <c>fields.*</c>.</param>
    /// <param name="details">The details fields extracted so far (details <c>text</c> fields), which win over the item's.</param>
    private static object? RenderText(CompiledTemplate template, Dictionary<string, object?> values, Dictionary<string, object?>? details, IReadOnlyDictionary<string, object?> vars, Uri? url, CancellationToken cancellationToken)
    {
        var fields = details is null ? values : new Dictionary<string, object?>(values);
        foreach (var (name, value) in details ?? [])
        {
            fields[name] = value;
        }

        var model = new Dictionary<string, object?>
        {
            ["fields"] = fields,
            ["vars"] = vars,
            ["request"] = new Dictionary<string, object?> { ["url"] = url?.AbsoluteUri },
        };
        foreach (var (name, value) in fields)
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

    private static object? ParseDocument(DocumentFormat format, JsonPath? htmlPath, FetchedDocument document)
    {
        if (format == DocumentFormat.Html && htmlPath is null)
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

        if (htmlPath is null)
        {
            return root;
        }

        return Select(htmlPath, root) is string text
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

    private CompiledDetails CompileDetails(DetailsDefinition details, DocumentFormat sourceFormat)
    {
        var format = details.Format ?? sourceFormat;
        var html = format == DocumentFormat.Html;
        foreach (var reserved in new[] { ReservedFields.Key, ReservedFields.Raw })
        {
            if (details.Fields.Any(f => f.Name == reserved))
            {
                throw new ExtractionDefinitionException($"details.fields.{reserved}: can't be set by details");
            }
        }

        if (!html && details.HtmlPath is not null)
        {
            throw new ExtractionDefinitionException("details.htmlPath: only applies to HTML details");
        }

        return new CompiledDetails(
            details,
            CompileTemplate(details.Url, "details.url"),
            format,
            details.HtmlPath is { } htmlPath ? CompilePath(htmlPath, "details.htmlPath") : null,
            details.Fields.Select(f => CompileField(f, html, "details.fields")).ToList());
    }

    private CompiledField CompileField(FieldDefinition field, bool html, string parent)
    {
        var location = $"{parent}.{field.Name}";
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

        public List<PendingItem> Items { get; } = [];

        public List<Uri> Urls { get; } = [];

        public List<string> Warnings { get; } = [];

        public HashSet<string> Keys { get; } = [];
    }
}

public sealed class CompiledExtraction
{
    internal CompiledExtraction(
        DocumentFormat format, IReadOnlyList<CompiledRequest> requests, JsonPath? items, CssSelector? itemSelector, JsonPath? htmlPath, IReadOnlyList<CompiledField> fields, CompiledDetails? details)
    {
        Format = format;
        Requests = requests;
        Items = items;
        ItemSelector = itemSelector;
        HtmlPath = htmlPath;
        Fields = fields;
        Details = details;
    }

    internal DocumentFormat Format { get; }

    internal IReadOnlyList<CompiledRequest> Requests { get; }

    /// <summary>JSON items.</summary>
    internal JsonPath? Items { get; }

    /// <summary>HTML items.</summary>
    internal CssSelector? ItemSelector { get; }

    internal JsonPath? HtmlPath { get; }

    internal IReadOnlyList<CompiledField> Fields { get; }

    internal CompiledDetails? Details { get; }
}

internal sealed class CompiledDetails(DetailsDefinition definition, CompiledTemplate url, DocumentFormat format, JsonPath? htmlPath, IReadOnlyList<CompiledField> fields)
{
    /// <summary>Changes with the details definition, so that editing it doesn't reuse stale details.</summary>
    private readonly string _definitionKey = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(definition)));

    public DetailsDefinition Definition { get; } = definition;

    public CompiledTemplate Url { get; } = url;

    public DocumentFormat Format { get; } = format;

    public JsonPath? HtmlPath { get; } = htmlPath;

    public IReadOnlyList<CompiledField> Fields { get; } = fields;

    /// <summary>Definition, URL and the item's fields (raw item included): an item whose list entry changed is fetched again.</summary>
    public string CacheKey(Uri url, JsonObject itemFields) =>
        _definitionKey + "|" + url.AbsoluteUri + "|" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(itemFields.ToJsonString())));
}

/// <summary>An item being extracted: fields first, then details, then the event.</summary>
internal sealed class PendingItem(int index, Uri? url, IReadOnlyDictionary<string, object?> vars)
{
    public int Index { get; } = index;

    public Uri? Url { get; } = url;

    public IReadOnlyDictionary<string, object?> Vars { get; } = vars;

    public Dictionary<string, object?> Values { get; } = [];

    public JsonObject Fields { get; } = [];

    public List<string> Errors { get; } = [];
}

internal sealed record CompiledRequest(RequestDefinition Definition, CompiledTemplate Url, IReadOnlyList<KeyValuePair<string, CompiledTemplate>> Foreach, JsonPath? Next, CssSelector? NextSelector);

internal sealed record CompiledField(FieldDefinition Definition, JsonPath? Path, CssSelector? Selector, CssSelector? Remove, CompiledTemplate? Text, IReadOnlyList<FieldFilter> Filters);
