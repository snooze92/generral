using Generral.Core.Sources;
using Generral.Core.Tv;

namespace Generral.Core.Mappings;

/// <summary>Joins an event (and optional part) to the episode a mapping produced from it. Keeps numbering stable.</summary>
public class EpisodeLink
{
    /// <summary>Stored for links without a part, so that the unique index also covers them (SQLite treats NULLs as distinct).</summary>
    public const string NoPart = "";

    public int Id { get; set; }

    public int MappingId { get; set; }

    public Mapping? Mapping { get; set; }

    public int EventId { get; set; }

    public Event? Event { get; set; }

    /// <summary>e.g. <c>main-card</c>, or <see cref="NoPart"/> when the mapping does not expand parts.</summary>
    public string PartKey { get; set; } = NoPart;

    public int EpisodeId { get; set; }

    public Episode? Episode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
