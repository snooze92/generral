// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text.Json.Nodes;

namespace Generral.Core.Mappings;

/// <summary>The parsed <see cref="Mapping.Definition"/> (docs/specs/05-mapping.md § Shape). Every string is a Scriban template.</summary>
public sealed record MappingDefinition
{
    public const string DefaultAirDate = "{{ event.start }}";

    /// <summary>All must be truthy for an event to be kept.</summary>
    public IReadOnlyList<string> Filter { get; init; } = [];

    /// <summary>Evaluated in order; later vars can reference earlier ones.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Vars { get; init; } = [];

    public SeasonDefinition Season { get; init; } = new();

    public EpisodeDefinition Episode { get; init; } = new();

    public string? Absolute { get; init; }

    public required string Title { get; init; }

    public string? Overview { get; init; }

    public string AirDate { get; init; } = DefaultAirDate;

    public string? Runtime { get; init; }

    /// <summary>Image kind (<c>thumb</c>, <c>poster</c>, …) → URL template.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Images { get; init; } = [];

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

    /// <summary>Season number template for <see cref="SeasonStrategy.Field"/> and <see cref="SeasonStrategy.Fixed"/>.</summary>
    public string? Value { get; init; }

    /// <summary>Grouping key template for <see cref="SeasonStrategy.Sequential"/>.</summary>
    public string? Group { get; init; }

    /// <summary>Optional display name (<c>&lt;namedseason&gt;</c>).</summary>
    public string? Title { get; init; }
}

public enum EpisodeStrategy
{
    Sequential,
    Field,
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

    /// <summary>Non-template settings (<c>categories</c>, <c>queryMode</c>, <c>dateWindow</c>), copied as-is.</summary>
    public JsonObject Settings { get; init; } = [];
}

public sealed class MappingDefinitionException(string message) : Exception(message);
