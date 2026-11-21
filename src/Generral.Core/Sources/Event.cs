using System.Text.Json.Nodes;
using Generral.Core.Datastore;

namespace Generral.Core.Sources;

/// <summary>A normalised record produced by a source, upserted by <c>(SourceId, Key)</c>.</summary>
public class Event
{
    public int Id { get; set; }

    public int SourceId { get; set; }

    public Source? Source { get; set; }

    public required string Key { get; set; }

    public required string Title { get; set; }

    public DateTime? Start { get; set; }

    public DateTime? End { get; set; }

    /// <summary>Original IANA timezone, if known.</summary>
    public string? Timezone { get; set; }

    public EventStatus Status { get; set; } = EventStatus.Unknown;

    /// <summary>All extracted fields, plus the (truncated) raw payload under <c>_raw</c>.</summary>
    public JsonObject Fields { get; set; } = [];

    public ImageSet Images { get; set; } = new();

    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;

    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public DateTime LastChanged { get; set; } = DateTime.UtcNow;

    /// <summary>Consecutive successful refreshes that did not return this event; at 3 it becomes <see cref="EventStatus.Gone"/>.</summary>
    public int MissedRefreshes { get; set; }

    public required string Hash { get; set; }
}

public enum EventStatus
{
    Scheduled,
    Postponed,
    Cancelled,
    Completed,
    Unknown,
    Gone
}
