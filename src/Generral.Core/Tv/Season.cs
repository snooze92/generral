using Generral.Core.Datastore;

namespace Generral.Core.Tv;

/// <summary>Identified by <c>(SeriesId, SeasonNumber)</c>. <c>0</c> = Specials; numbers may be years.</summary>
public class Season
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    public int SeasonNumber { get; set; }

    /// <summary>Optional display name (e.g. <c>2024-25</c>), written as <c>&lt;namedseason&gt;</c>.</summary>
    public string? Title { get; set; }

    public bool Monitored { get; set; } = true;

    public ImageSet Images { get; set; } = new();
}
