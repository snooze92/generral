using System.Text.Json.Nodes;
using Generral.Core.Datastore;

namespace Generral.Core.Sources.Extraction;

/// <summary>An event's core attributes, normalised from the reserved fields.</summary>
public sealed record ExtractedEvent
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public DateTime? Start { get; init; }

    public DateTime? End { get; init; }

    public string? Timezone { get; init; }

    public EventStatus Status { get; init; } = EventStatus.Unknown;

    /// <summary>All extracted fields, plus the (truncated) raw item under <c>_raw</c>.</summary>
    public required JsonObject Fields { get; init; }

    public ImageSet Images { get; init; } = new();
}

/// <param name="Index">Position of the item across all fetched documents.</param>
/// <param name="Url">The document the item came from, if any.</param>
/// <param name="Event">Null when the item has errors.</param>
public sealed record ExtractedItem(int Index, Uri? Url, JsonObject Fields, ExtractedEvent? Event, IReadOnlyList<string> Errors);

public sealed record ExtractionResult(IReadOnlyList<ExtractedItem> Items, IReadOnlyList<Uri> Urls, IReadOnlyList<string> Warnings)
{
    public IEnumerable<ExtractedEvent> Events => Items.Where(i => i.Event is not null).Select(i => i.Event!);
}

/// <summary>The extraction as a whole failed (bad URL, invalid document, request cap): never marks events gone.</summary>
public sealed class ExtractionException(string message, Exception? innerException = null) : Exception(message, innerException);
