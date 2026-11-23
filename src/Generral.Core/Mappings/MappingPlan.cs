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

    /// <summary>
    /// Series → Renumber: unlocked episodes take their ideal number (as with <c>onInsert: renumber-unlocked</c>), whatever
    /// the mapping's <c>onInsert</c>. Episodes of <c>field</c>-strategy mappings keep their numbers.
    /// </summary>
    public bool Renumber { get; init; }

    /// <summary>The <c>dedupe</c> keys the series' other mappings produce.</summary>
    public IReadOnlyList<DedupeClaim> Claims { get; init; } = [];

    public required DateTime Now { get; init; }

    /// <summary>Series timezone: gives <c>Episode.AirDate</c> and the year of <see cref="SeasonStrategy.Year"/> seasons.</summary>
    public TimeZoneInfo Timezone { get; init; } = TimeZoneInfo.Local;
}

/// <summary>An event (part) another mapping of the series keeps, by <c>dedupe</c> key.</summary>
/// <param name="HigherPriority">The other mapping wins: this mapping skips the event.</param>
/// <param name="EpisodeId">The episode the other mapping links, if any: a lower-priority one is taken over.</param>
public sealed record DedupeClaim(string Key, string PartKey, string MappingName, bool HigherPriority, int? EpisodeId);

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

    /// <summary>A higher-priority mapping of the series keeps an event with the same <c>dedupe</c> key: the event is skipped.</summary>
    Duplicate,
}

/// <summary>What the mapping does with one event.</summary>
public sealed class MappingPlanRow
{
    private readonly List<string> _errors = [];

    internal MappingPlanRow(Event ev, string? partKey, Episode? existing)
    {
        Event = ev;
        PartKey = partKey;
        EpisodeId = existing?.Id;
        IsLinked = existing is not null;
    }

    public Event Event { get; }

    /// <summary>The part this row is about, or null when the mapping does not expand parts (or the event was not expanded).</summary>
    public string? PartKey { get; }

    /// <summary>Rendered <c>part.title</c>.</summary>
    public string? PartTitle { get; internal set; }

    /// <summary>The episode linked to the event (and part), if any; or the episode taken over from another link (see <see cref="IsLinked"/>).</summary>
    public int? EpisodeId { get; internal set; }

    /// <summary>
    /// False when <see cref="EpisodeId"/> is taken over from a lower-priority mapping's link with the same <c>dedupe</c> key
    /// and part: applying moves that link to this mapping.
    /// </summary>
    public bool IsLinked { get; internal set; }

    /// <summary>Rendered <c>dedupe</c> key.</summary>
    public string? DedupeKey { get; internal set; }

    /// <summary>The higher-priority mapping that keeps the same event (<see cref="MappingChange.Duplicate"/>).</summary>
    public string? DuplicateOf { get; internal set; }

    public bool Kept { get; internal set; }

    /// <summary>The filter template that rejected the event, or <c>event is gone</c>.</summary>
    public string? RejectedBy { get; internal set; }

    public int? SeasonNumber { get; internal set; }

    public string? SeasonTitle { get; internal set; }

    /// <summary>Rendered <c>season.images.poster</c>.</summary>
    public string? SeasonPoster { get; internal set; }

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

    /// <summary>Monitored state for a new episode (the part's <c>monitored</c>); null keeps the default.</summary>
    public bool? Monitored { get; internal set; }

    public IReadOnlyList<string> Errors => _errors;

    public MappingChange Change { get; internal set; }

    /// <summary>Kept, without errors: the episode is created or updated.</summary>
    public bool IsApplied => Kept && _errors.Count == 0 && DuplicateOf is null;

    /// <summary>The link this row is stored as.</summary>
    public string LinkPartKey => PartKey ?? EpisodeLink.NoPart;

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

    /// <summary>Event-level outcome (filter, vars) shared by the rows of an event's parts.</summary>
    internal void CopyStateFrom(MappingPlanRow other)
    {
        Kept = other.Kept;
        RejectedBy = other.RejectedBy;
        DedupeKey = other.DedupeKey;
        _errors.AddRange(other._errors);
    }
}

/// <param name="Rows">One row per event, or per event and part (including parts that are no longer produced).</param>
/// <param name="Seasons">Season number → display name (<c>season.title</c>), for every season an applied row uses.</param>
public sealed record MappingPlan(IReadOnlyList<MappingPlanRow> Rows, SeriesType SeriesType, IReadOnlyDictionary<int, string?> Seasons)
{
    /// <summary>Season number → poster URL (<c>season.images.poster</c>, first non-empty in event order); seasons without one are absent.</summary>
    public IReadOnlyDictionary<int, string> SeasonPosters { get; init; } = new Dictionary<int, string>();
}
