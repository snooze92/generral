using System.Text.Json.Nodes;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Http;

namespace Generral.Core.Sources.Types;

/// <summary>A source type plugin (see docs/specs/04-sources.md § Source type plugins).</summary>
public interface ISourceType
{
    /// <summary>Stored in <see cref="Source.Type"/>, e.g. <c>thesportsdb</c>.</summary>
    string Id { get; }

    string Name { get; }

    /// <summary>Settings schema, rendered as a provider form. Values are the defaults.</summary>
    IReadOnlyList<SourceField> Fields { get; }

    IReadOnlyList<SourceValidationFailure> Validate(JsonObject settings);

    /// <exception cref="SourceFetchException">Invalid settings, or the fetch as a whole failed.</exception>
    Task<SourceFetchResult> FetchAsync(JsonObject settings, SourceFetchOptions? options = null, CancellationToken cancellationToken = default);
}

public enum SourceFieldType
{
    Textbox,
    TextArea,
    Number,
    Password,
    Checkbox,
    Select,
    KeyValueList,
}

/// <summary>A settings field, in the shape of Sonarr's provider <c>fields[]</c>.</summary>
public sealed record SourceField
{
    public required string Name { get; init; }

    public required string Label { get; init; }

    public SourceFieldType Type { get; init; } = SourceFieldType.Textbox;

    public JsonNode? Value { get; init; }

    public string? HelpText { get; init; }

    public string? Unit { get; init; }

    public string? Placeholder { get; init; }

    public bool Advanced { get; init; }

    public IReadOnlyList<SourceSelectOption>? SelectOptions { get; init; }
}

public sealed record SourceSelectOption(string Value, string Name, string? Hint = null);

public sealed record SourceValidationFailure(string PropertyName, string ErrorMessage);

public sealed record SourceFetchOptions
{
    /// <summary>Skip the raw-response cache (responses are still stored in it).</summary>
    public bool BypassCache { get; init; }
}

public sealed record SourceFetchResult
{
    public required IReadOnlyList<ExtractedEvent> Events { get; init; }

    public required IReadOnlyList<SourceItemError> ItemErrors { get; init; }

    public required IReadOnlyList<RawResponse> Responses { get; init; }

    /// <summary>Source-level data for mappings (<c>source.meta.*</c>), e.g. the TheSportsDB league.</summary>
    public required JsonObject Meta { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>An item that could not become an event.</summary>
public sealed record SourceItemError(int Index, Uri? Url, JsonObject Fields, IReadOnlyList<string> Errors);

/// <summary>The fetch as a whole failed: never marks events gone.</summary>
public sealed class SourceFetchException(string message, IReadOnlyList<RawResponse>? responses = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public IReadOnlyList<RawResponse> Responses { get; } = responses ?? [];
}
