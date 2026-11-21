using Generral.Core.Sources;
using Generral.Core.Tv;

namespace Generral.Core.Mappings;

/// <summary>Turns a source's events into a series' episodes (spec 05).</summary>
public class Mapping
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    public int SourceId { get; set; }

    public Source? Source { get; set; }

    /// <summary>Lower wins when two mappings want the same slot.</summary>
    public int Priority { get; set; }

    /// <summary>
    /// YAML (or JSON) as authored, kept verbatim: <c>filter</c>, <c>vars</c>, <c>season</c>, <c>episode</c>, <c>title</c>, …, <c>search</c>.
    /// Read with <see cref="MappingDefinitionParser.FromYaml"/>.
    /// </summary>
    public string Definition { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>Incremented on every definition change; each version is kept as a <see cref="MappingVersion"/>.</summary>
    public int Version { get; set; } = 1;
}
