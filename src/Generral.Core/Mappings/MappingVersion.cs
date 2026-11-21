using System.Text.Json.Nodes;

namespace Generral.Core.Mappings;

/// <summary>A saved definition of a mapping, kept for diff and rollback. Unique on <c>(MappingId, Version)</c>.</summary>
public class MappingVersion
{
    public int Id { get; set; }

    public int MappingId { get; set; }

    public Mapping? Mapping { get; set; }

    public int Version { get; set; }

    public JsonObject Definition { get; set; } = [];

    public DateTime Created { get; set; } = DateTime.UtcNow;
}
