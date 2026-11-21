using System.Text.Json.Nodes;
using Generral.Core.Sources;
using Generral.Core.Tv;

namespace Generral.Core.Mappings;

/// <summary>Everything <see cref="MappingEngine.Evaluate"/> needs. Pure data: nothing is read from or written to the database.</summary>
public sealed record MappingInput
{
    public required MappingDefinition Definition { get; init; }

    public required Series Series { get; init; }

    public required Source Source { get; init; }

    /// <summary>The source's events, including gone ones.</summary>
    public required IReadOnlyList<Event> Events { get; init; }

    /// <summary><c>source.meta</c>: source-level data from the last fetch.</summary>
    public JsonObject? SourceMeta { get; init; }

    /// <summary>All episodes of the series (any mapping, manual, orphaned).</summary>
    public IReadOnlyList<Episode> Episodes { get; init; } = [];

    /// <summary>The links of this mapping.</summary>
    public IReadOnlyList<EpisodeLink> Links { get; init; } = [];

    public required DateTime Now { get; init; }

    /// <summary>Series timezone: gives <c>Episode.AirDate</c> and the year of <see cref="SeasonStrategy.Year"/> seasons.</summary>
    public TimeZoneInfo Timezone { get; init; } = TimeZoneInfo.Local;
}

public enum MappingChange
{
    /// <summary>A new episode is created.</summary>
    New,

    Changed,

    Unchanged,

    /// <summary>The event is gone or no longer passes the filter: its episode becomes orphaned.</summary>
    Orphaned,

    /// <summary>The event does not pass the filter and has no episode.</summary>
    Filtered,

    /// <summary>The event could not be mapped (see <see cref="MappingPlanRow.Errors"/>); its episode, if any, is left as is.</summary>
    Error,
}

/// <summary>What the mapping does with one event.</summary>
public sealed class MappingPlanRow
{
    private readonly List<string> _errors = [];

    internal MappingPlanRow(Event ev, Episode? existing)
    {
        Event = ev;
        EpisodeId = existing?.Id;
    }

    public Event Event { get; }

    /// <summary>The episode already linked to the event, if any.</summary>
    public int? EpisodeId { get; }

    public bool Kept { get; internal set; }

    /// <summary>The filter template that rejected the event, or <c>event is gone</c>.</summary>
    public string? RejectedBy { get; internal set; }

    public int? SeasonNumber { get; internal set; }

    public string? SeasonTitle { get; internal set; }

    public int? EpisodeNumber { get; internal set; }

    public int? AbsoluteEpisodeNumber { get; internal set; }

    public string? Title { get; internal set; }

    public string? Overview { get; internal set; }

    public DateTime? AirDateUtc { get; internal set; }

    public DateOnly? AirDate { get; internal set; }

    public int? Runtime { get; internal set; }

    public string? Thumb { get; internal set; }

    public JsonObject SearchTerms { get; internal set; } = [];

    public bool NumberLocked { get; internal set; }

    public IReadOnlyList<string> Errors => _errors;

    public MappingChange Change { get; internal set; }

    /// <summary>Kept, without errors: the episode is created or updated.</summary>
    public bool IsApplied => Kept && _errors.Count == 0;

    /// <summary>Copies the planned values to the event's episode (new or existing) and marks it active.</summary>
    public void ApplyTo(Episode episode)
    {
        if (!IsApplied)
        {
            throw new InvalidOperationException($"Row of event '{Event.Key}' is not applied");
        }

        episode.SeasonNumber = SeasonNumber!.Value;
        episode.EpisodeNumber = EpisodeNumber!.Value;
        episode.AbsoluteEpisodeNumber = AbsoluteEpisodeNumber;
        episode.Title = Title!;
        episode.Overview = Overview;
        episode.AirDateUtc = AirDateUtc;
        episode.AirDate = AirDate;
        episode.Runtime = Runtime;
        episode.Images = episode.Images with { Thumb = Thumb };
        episode.SearchTerms = (JsonObject)SearchTerms.DeepClone();
        episode.NumberLocked = NumberLocked;
        episode.Status = EpisodeStatus.Active;
    }

    internal void AddError(string error) => _errors.Add(error);
}

/// <param name="Seasons">Season number → display name (<c>season.title</c>), for every season an applied row uses.</param>
public sealed record MappingPlan(IReadOnlyList<MappingPlanRow> Rows, SeriesType SeriesType, IReadOnlyDictionary<int, string?> Seasons);
