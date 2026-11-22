using System.Text.Json.Nodes;

namespace Generral.Core.Sources.Extraction;

/// <summary>Requests → items → fields (see docs/specs/04-sources.md § Extraction model).</summary>
public sealed record ExtractionDefinition
{
    public required IReadOnlyList<RequestDefinition> Requests { get; init; }

    public DocumentFormat Format { get; init; } = DocumentFormat.Json;

    /// <summary>JSONPath (JSON) or CSS selector (HTML) selecting the items in each response.</summary>
    public required string Items { get; init; }

    /// <summary>HTML only: JSONPath to the HTML string in a JSON response (e.g. MediaWiki's parse API). Null when the response is HTML.</summary>
    public string? HtmlPath { get; init; }

    /// <summary>Evaluated in order: <see cref="FieldDefinition.Text"/> templates see the fields before them.</summary>
    public required IReadOnlyList<FieldDefinition> Fields { get; init; }
}

public sealed record RequestDefinition
{
    /// <summary>Scriban template, may reference the <see cref="Foreach"/> variables.</summary>
    public required string Url { get; init; }

    /// <summary>Variable name → template evaluating to a value, an array or a range. One request per combination.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Foreach { get; init; } = [];

    public PaginationDefinition? Pagination { get; init; }
}

public sealed record PaginationDefinition
{
    public const int DefaultMaxPages = 10;
    public const int MaxPagesLimit = 100;

    /// <summary>JSONPath (HTML: CSS selector of a link, its <c>href</c>) to the next page URL (absolute or relative to the current one).</summary>
    public string? Next { get; init; }

    /// <summary>Query parameter set to the page number, starting at <see cref="Start"/>.</summary>
    public string? PageParam { get; init; }

    public int Start { get; init; } = 1;

    /// <summary>Stop at the first page without items (page-number mode).</summary>
    public bool UntilEmpty { get; init; } = true;

    /// <summary>Total pages fetched per request, including the first one.</summary>
    public int MaxPages { get; init; } = DefaultMaxPages;
}

public sealed record FieldDefinition
{
    public required string Name { get; init; }

    /// <summary>JSONPath relative to the item (JSON).</summary>
    public string? Path { get; init; }

    /// <summary>CSS selector relative to the item (HTML); comma-separated alternatives take the first that matches.</summary>
    public string? Selector { get; init; }

    /// <summary>With <see cref="Selector"/>: read this attribute instead of the text.</summary>
    public string? Attribute { get; init; }

    /// <summary>With <see cref="Selector"/>: CSS selector of sub-elements left out of the text (e.g. <c>sup</c>).</summary>
    public string? Remove { get; init; }

    /// <summary>Scriban template over the previous fields (and the <c>foreach</c> variables).</summary>
    public string? Text { get; init; }

    public IReadOnlyList<FilterDefinition> Filters { get; init; } = [];

    public bool Optional { get; init; }

    public JsonNode? Default { get; init; }
}

public enum DocumentFormat
{
    Json,
    Html,
}

public sealed record FilterDefinition(string Name, IReadOnlyList<string> Args);

public sealed class ExtractionDefinitionException(string message) : Exception(message);
