using System.Text.Json.Nodes;
using Generral.Core.Datastore;

namespace Generral.Core.Sources;

/// <summary>Snapshot of an event as it was before a change (its hash differed on refresh). Kept for 30 days.</summary>
public class EventRevision
{
    public int Id { get; set; }

    public int EventId { get; set; }

    public Event? Event { get; set; }

    public DateTime Created { get; set; } = DateTime.UtcNow;

    public required string Title { get; set; }

    public DateTime? Start { get; set; }

    public DateTime? End { get; set; }

    public EventStatus Status { get; set; }

    public JsonObject Fields { get; set; } = [];

    public ImageSet Images { get; set; } = new();

    public required string Hash { get; set; }
}
