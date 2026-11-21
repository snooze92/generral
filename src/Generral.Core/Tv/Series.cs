using System.Text.Json.Nodes;
using Generral.Core.Datastore;

namespace Generral.Core.Tv;

public class Series
{
    public int Id { get; set; }

    public required string Title { get; set; }

    public required string SortTitle { get; set; }

    /// <summary>Used for path generation and matching.</summary>
    public required string CleanTitle { get; set; }

    public required string TitleSlug { get; set; }

    public int? Year { get; set; }

    public string? Overview { get; set; }

    public string? Network { get; set; }

    public List<string> Genres { get; set; } = [];

    public SeriesStatus Status { get; set; } = SeriesStatus.Continuing;

    public ImageSet Images { get; set; } = new();

    public SeriesType SeriesType { get; set; } = SeriesType.Standard;

    public required string Path { get; set; }

    public required string RootFolderPath { get; set; }

    public int QualityProfileId { get; set; }

    public bool Monitored { get; set; } = true;

    public NewItemMonitorType MonitorNewItems { get; set; } = NewItemMonitorType.All;

    public bool SeasonFolder { get; set; } = true;

    public List<int> Tags { get; set; } = [];

    /// <summary>Default search query templates and match rules, overridable per mapping (spec 06).</summary>
    public JsonObject SearchSettings { get; set; } = [];

    /// <summary>Stable id written to NFO <c>&lt;uniqueid type="generral"&gt;</c>.</summary>
    public string Guid { get; set; } = NewGuid();

    public DateTime Added { get; set; } = DateTime.UtcNow;

    public static string NewGuid() => $"generral://series/{System.Guid.NewGuid()}";
}

public enum SeriesStatus
{
    Continuing,
    Ended
}

public enum SeriesType
{
    /// <summary>SxxEyy numbering.</summary>
    Standard,

    /// <summary>Date-based numbering.</summary>
    Daily
}

public enum NewItemMonitorType
{
    All,
    None
}
