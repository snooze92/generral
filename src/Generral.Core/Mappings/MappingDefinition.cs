using System.Text.Json.Nodes;

namespace Generral.Core.Mappings;

/// <summary>The parsed <see cref="Mapping.Definition"/> (docs/specs/05-mapping.md § Shape). Every string is a Scriban template.</summary>
public sealed record MappingDefinition
{
    public const string DefaultTitle = "{{ event.title }}";
    public const string DefaultAirDate = "{{ event.start }}";

    /// <summary>Default <see cref="Title"/> of a mapping with <see cref="Parts"/>.</summary>
    public const string DefaultPartTitle = "{{ event.title }} - {{ part.title }}";

    /// <summary>All must be truthy for an event to be kept.</summary>
    public IReadOnlyList<string> Filter { get; init; } = [];

    /// <summary>Evaluated in order; later vars can reference earlier ones.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Vars { get; init; } = [];

    /// <summary>Optional: each kept event becomes one episode per part instead of one episode.</summary>
    public IReadOnlyList<PartDefinition>? Parts { get; init; }

    /// <summary>
    /// Key of the real-world event. A kept event is skipped when a mapping of the same series with a higher priority
    /// keeps an event with the same key (docs/specs/05-mapping.md § Numbering stability, rule 6).
    /// </summary>
    public string? Dedupe { get; init; }

    public SeasonDefinition Season { get; init; } = new();

    public EpisodeDefinition Episode { get; init; } = new();

    public string? Absolute { get; init; }

    public string Title { get; init; } = DefaultTitle;

    public string? Overview { get; init; }

    public string AirDate { get; init; } = DefaultAirDate;

    public string? Runtime { get; init; }

    /// <summary>Episode thumbnail URL template.</summary>
    public string? Thumb { get; init; }

    public SearchDefinition Search { get; init; } = new();
}

public enum SeasonStrategy
{
    Year,
    Field,
    Fixed,
    Sequential,
}

public sealed record SeasonDefinition
{
    public SeasonStrategy Strategy { get; init; } = SeasonStrategy.Year;

    /// <summary>Season number template for <see cref="SeasonStrategy.Field"/>; an integer for <see cref="SeasonStrategy.Fixed"/>.</summary>
    public string? Value { get; init; }

    /// <summary>Grouping key template for <see cref="SeasonStrategy.Sequential"/>.</summary>
    public string? Group { get; init; }

    /// <summary>Optional display name (<c>&lt;namedseason&gt;</c>).</summary>
    public string? Title { get; init; }

    /// <summary>
    /// Season poster URL template (<c>season.images.poster</c>), rendered per event like <see cref="Title"/>: the season takes the first
    /// non-empty value in event order (earliest first).
    /// </summary>
    public string? Poster { get; init; }
}

public enum EpisodeStrategy
{
    Sequential,
    Field,

    /// <summary>
    /// The series becomes daily: files and Plex use the air date. Episodes still get an internal number, assigned like
    /// <see cref="Sequential"/>, because <c>(season, episode)</c> is unique.
    /// </summary>
    Date,
}

public enum InsertPolicy
{
    /// <summary>New events take <c>max+1</c> in their season.</summary>
    Append,

    /// <summary>Episodes whose number is not locked are renumbered by <c>order</c>; locked ones keep theirs.</summary>
    RenumberUnlocked,
}

public sealed record EpisodeDefinition
{
    public const string DefaultOrder = "{{ event.start }}";

    public EpisodeStrategy Strategy { get; init; } = EpisodeStrategy.Sequential;

    /// <summary>Sort key within a season (dates, numbers or text).</summary>
    public string Order { get; init; } = DefaultOrder;

    /// <summary>Episode number template for <see cref="EpisodeStrategy.Field"/>.</summary>
    public string? Value { get; init; }

    public InsertPolicy OnInsert { get; init; } = InsertPolicy.Append;
}

/// <summary>Rendered into <c>Episode.SearchTerms</c>; the indexer side is spec 06.</summary>
public sealed record SearchDefinition
{
    public IReadOnlyList<string> Queries { get; init; } = [];

    public IReadOnlyList<string> Must { get; init; } = [];

    public IReadOnlyList<string> MustNot { get; init; } = [];

    public IReadOnlyList<string> Any { get; init; } = [];

    /// <summary>Rules added for one part (by key), merged with the event-level ones.</summary>
    public IReadOnlyDictionary<string, PartSearchDefinition> Parts { get; init; } = new Dictionary<string, PartSearchDefinition>();

    /// <summary>Non-template settings (<c>categories</c>, <c>queryMode</c>, <c>dateWindow</c>, <c>searchSchedule</c>), copied as-is.</summary>
    public JsonObject Settings { get; init; } = [];
}

public sealed record PartDefinition
{
    /// <summary>Stored in <see cref="EpisodeLink.PartKey"/>.</summary>
    public required string Key { get; init; }

    /// <summary>The part is only produced when this is truthy.</summary>
    public string? When { get; init; }

    /// <summary>Monitored state of new episodes of this part.</summary>
    public string? Monitored { get; init; }

    /// <summary>Overrides the mapping's <see cref="MappingDefinition.AirDate"/>.</summary>
    public string? AirDate { get; init; }

    /// <summary>Overrides the mapping's <see cref="MappingDefinition.Runtime"/>.</summary>
    public string? Runtime { get; init; }

    public PartSearchDefinition Search { get; init; } = new();

    /// <summary>Every property but <c>search</c>, as authored (e.g. <c>title</c>, custom keys): available to templates as <c>part.*</c>.</summary>
    public JsonObject Values { get; init; } = [];
}

/// <summary>Part-level search rules: <c>must</c>, <c>mustNot</c> and <c>any</c> add to the event's, <c>queries</c> replace them.</summary>
public sealed record PartSearchDefinition
{
    public IReadOnlyList<string> Queries { get; init; } = [];

    public IReadOnlyList<string> Must { get; init; } = [];

    public IReadOnlyList<string> MustNot { get; init; } = [];

    public IReadOnlyList<string> Any { get; init; } = [];
}

public sealed class MappingDefinitionException(string message) : Exception(message);
