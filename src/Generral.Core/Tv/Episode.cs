using System.Text.Json.Nodes;
using Generral.Core.Datastore;

namespace Generral.Core.Tv;

public class Episode
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public int? AbsoluteEpisodeNumber { get; set; }

    public required string Title { get; set; }

    public string? Overview { get; set; }

    public DateTime? AirDateUtc { get; set; }

    /// <summary>Local date in the series timezone, used for daily series and NFO <c>&lt;aired&gt;</c>.</summary>
    public DateOnly? AirDate { get; set; }

    /// <summary>Minutes.</summary>
    public int? Runtime { get; set; }

    public bool Monitored { get; set; } = true;

    public int? EpisodeFileId { get; set; }

    public bool HasFile => EpisodeFileId != null;

    public ImageSet Images { get; set; } = new();

    /// <summary>Rendered query strings and match rules, computed from the mapping and cached.</summary>
    public JsonObject SearchTerms { get; set; } = [];

    public DateTime? LastSearchTime { get; set; }

    /// <summary>Set once the number has been published; mappings then never change it (spec 05 § Numbering stability).</summary>
    public bool NumberLocked { get; set; }

    public EpisodeSource Source { get; set; } = EpisodeSource.Mapping;

    public EpisodeStatus Status { get; set; } = EpisodeStatus.Active;
}

public enum EpisodeSource
{
    Mapping,

    /// <summary>User-created, never touched by mappings.</summary>
    Manual
}

public enum EpisodeStatus
{
    Active,

    /// <summary>No longer produced by any mapping (event gone or filtered out).</summary>
    Orphaned
}
