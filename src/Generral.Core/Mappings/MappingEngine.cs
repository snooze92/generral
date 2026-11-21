using System.Globalization;
using System.Text.Json.Nodes;
using Generral.Core.Sources;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Types;
using Generral.Core.Templating;
using Generral.Core.Tv;

namespace Generral.Core.Mappings;

/// <summary>
/// Computes what a mapping does with a source's events (docs/specs/05-mapping.md), without touching the database.
/// Numbering follows § Numbering stability: links are sticky, new events are numbered by <c>onInsert</c>,
/// locked and orphaned numbers are never reused.
/// </summary>
public sealed class MappingEngine(TemplateRenderer renderer)
{
    public const string GoneReason = "event is gone";

    /// <summary>Compiles every template of the definition.</summary>
    /// <exception cref="MappingDefinitionException">A template has syntax errors.</exception>
    public void Validate(MappingDefinition definition) => _ = new Templates(renderer, definition);

    /// <exception cref="MappingDefinitionException">A template has syntax errors.</exception>
    public MappingPlan Evaluate(MappingInput input, CancellationToken cancellationToken = default)
    {
        var templates = new Templates(renderer, input.Definition);
        var episodes = input.Episodes.ToDictionary(e => e.Id);
        var linked = input.Links
            .Where(l => l.PartKey == EpisodeLink.NoPart && episodes.ContainsKey(l.EpisodeId))
            .ToDictionary(l => l.EventId, l => episodes[l.EpisodeId]);
        var sourceTimezone = new SourceSettings(input.Source.Settings).Timezone();
        var shared = MappingTemplateModel.Create(input.Series, input.Source, input.SourceMeta);

        var rows = new List<RowState>();
        foreach (var ev in input.Events.OrderBy(e => e.Start ?? DateTime.MaxValue).ThenBy(e => e.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = new RowState(new MappingPlanRow(ev, linked.GetValueOrDefault(ev.Id)), linked.GetValueOrDefault(ev.Id),
                MappingTemplateModel.ForEvent(shared, ev, input.Source));
            rows.Add(row);
            Prepare(row, templates, input, sourceTimezone, cancellationToken);
        }

        var numbering = rows.Where(r => r.Plan.IsApplied).ToList();
        AssignSeasons(numbering, input);
        AssignEpisodes(numbering, input);

        var seasons = new Dictionary<int, string?>();
        foreach (var row in numbering)
        {
            Render(row, templates, input.Definition, cancellationToken);
            if (row.Plan.IsApplied)
            {
                var number = row.Plan.SeasonNumber!.Value;
                seasons[number] = seasons.GetValueOrDefault(number) ?? row.Plan.SeasonTitle;
            }
        }

        foreach (var row in rows)
        {
            row.Plan.Change = ChangeOf(row);
        }

        var seriesType = input.Definition.Episode.Strategy == EpisodeStrategy.Date ? SeriesType.Daily : SeriesType.Standard;
        return new MappingPlan(rows.Select(r => r.Plan).ToList(), seriesType, seasons);
    }

    /// <summary>Filter, vars, air date, season inputs and order key: everything that does not depend on other events.</summary>
    private static void Prepare(RowState row, Templates templates, MappingInput input, TimeZoneInfo sourceTimezone, CancellationToken cancellationToken)
    {
        var plan = row.Plan;
        if (plan.Event.Status == EventStatus.Gone)
        {
            plan.RejectedBy = GoneReason;
            return;
        }

        foreach (var filter in templates.Filter)
        {
            if (!Try(plan, "filter", () => IsTruthy(filter.Evaluate(row.Model, cancellationToken)), out var passed))
            {
                return;
            }

            if (!passed)
            {
                plan.RejectedBy = filter.Text;
                return;
            }
        }

        plan.Kept = true;
        var vars = (Dictionary<string, object?>)row.Model["vars"]!;
        foreach (var (name, template) in templates.Vars)
        {
            if (!Try(plan, $"vars.{name}", () => template.Evaluate(row.Model, cancellationToken), out var value))
            {
                return;
            }

            vars[name] = value;
        }

        if (Try(plan, "airDate", () => ToUtc(templates.AirDate.Evaluate(row.Model, cancellationToken), sourceTimezone), out var airDateUtc))
        {
            plan.AirDateUtc = airDateUtc;
            plan.AirDate = airDateUtc is { } utc ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, input.Timezone)) : null;
        }

        // File and grab locks are set elsewhere; the mapping locks numbers once the air date has passed.
        plan.NumberLocked = row.Existing?.NumberLocked == true || row.Existing?.AirDateUtc <= input.Now || plan.AirDateUtc <= input.Now;

        var season = input.Definition.Season;
        switch (season.Strategy)
        {
            case SeasonStrategy.Year when plan.AirDate is { } date:
                row.Season = date.Year;
                break;
            case SeasonStrategy.Year when plan.Errors.Count == 0:
                plan.AddError("season: the air date is required for year seasons");
                break;
            case SeasonStrategy.Fixed:
                row.Season = int.Parse(season.Value!, CultureInfo.InvariantCulture);
                break;
            case SeasonStrategy.Field:
                if (Try(plan, "season.value", () => ToInt(templates.SeasonValue!.Evaluate(row.Model, cancellationToken)), out var number))
                {
                    row.Season = number is >= 0 ? number : null;
                    if (row.Season is null)
                    {
                        plan.AddError("season.value: must be a season number");
                    }
                }

                break;
            case SeasonStrategy.Sequential:
                if (Try(plan, "season.group", () => templates.SeasonGroup!.Render(row.Model, cancellationToken), out var group))
                {
                    row.Group = group;
                }

                break;
        }

        if (Try(plan, "episode.order", () => templates.Order.Evaluate(row.Model, cancellationToken), out var order))
        {
            row.Order = order;
        }

        if (input.Definition.Episode.Strategy == EpisodeStrategy.Field
            && Try(plan, "episode.value", () => ToInt(templates.EpisodeValue!.Evaluate(row.Model, cancellationToken)), out var episode))
        {
            row.FieldNumber = episode is > 0 ? episode : null;
            if (row.FieldNumber is null)
            {
                plan.AddError("episode.value: must be a positive episode number");
            }
        }
    }

    /// <summary>Sequential seasons: groups already linked keep their season, new groups take the next numbers by first <c>order</c>.</summary>
    private static void AssignSeasons(List<RowState> rows, MappingInput input)
    {
        if (input.Definition.Season.Strategy != SeasonStrategy.Sequential)
        {
            return;
        }

        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows.Where(r => r.Existing is not null))
        {
            groups.TryAdd(row.Group!, row.Existing!.SeasonNumber);
        }

        var next = input.Episodes.Select(e => e.SeasonNumber).Append(0).Max() + 1;
        foreach (var row in rows.Where(r => !groups.ContainsKey(r.Group!)).OrderBy(r => r, OrderComparer.Instance))
        {
            if (!groups.ContainsKey(row.Group!))
            {
                groups[row.Group!] = next++;
            }
        }

        foreach (var row in rows)
        {
            row.Season = groups[row.Group!];
        }
    }

    private static void AssignEpisodes(List<RowState> rows, MappingInput input)
    {
        var episode = input.Definition.Episode;
        var renumber = episode.OnInsert == InsertPolicy.RenumberUnlocked && episode.Strategy != EpisodeStrategy.Field;
        var pool = new List<RowState>();
        foreach (var row in rows)
        {
            if (row.Existing is { } existing && (row.Plan.NumberLocked || !renumber))
            {
                row.Plan.SeasonNumber = existing.SeasonNumber;
                row.Plan.EpisodeNumber = existing.EpisodeNumber;
            }
            else
            {
                row.Plan.SeasonNumber = row.Season;
                pool.Add(row);
            }
        }

        // Every number held by an episode that is not being (re)numbered here: locked, orphaned, manual, other mappings.
        var poolIds = pool.Where(r => r.Existing is not null).Select(r => r.Existing!.Id).ToHashSet();
        var taken = input.Episodes
            .Where(e => !poolIds.Contains(e.Id))
            .Select(e => (e.SeasonNumber, e.EpisodeNumber))
            .ToHashSet();

        foreach (var seasonRows in pool.GroupBy(r => r.Season!.Value))
        {
            var season = seasonRows.Key;
            var ordered = seasonRows.OrderBy(r => r, OrderComparer.Instance).ToList();
            if (episode.Strategy == EpisodeStrategy.Field)
            {
                foreach (var row in ordered)
                {
                    if (!taken.Add((season, row.FieldNumber!.Value)))
                    {
                        var other = input.Episodes.FirstOrDefault(e => e.SeasonNumber == season && e.EpisodeNumber == row.FieldNumber);
                        row.Plan.AddError($"episode.value: S{season:00}E{row.FieldNumber:00} is already used by "
                                          + (other is null ? "another event" : $"episode '{other.Title}'"));
                        continue;
                    }

                    row.Plan.EpisodeNumber = row.FieldNumber;
                }

                continue;
            }

            var next = renumber ? 0 : taken.Where(t => t.SeasonNumber == season).Select(t => t.EpisodeNumber).Append(0).Max();
            foreach (var row in ordered)
            {
                do
                {
                    next++;
                }
                while (taken.Contains((season, next)));

                taken.Add((season, next));
                row.Plan.EpisodeNumber = next;
            }
        }
    }

    /// <summary>Templates that may use <c>season</c> and <c>episode</c>.</summary>
    private static void Render(RowState row, Templates templates, MappingDefinition definition, CancellationToken cancellationToken)
    {
        var plan = row.Plan;
        if (!plan.IsApplied)
        {
            return;
        }

        row.Model["season"] = plan.SeasonNumber;
        row.Model["episode"] = plan.EpisodeNumber;
        string Text(CompiledTemplate template) => template.Render(row.Model, cancellationToken).Trim();
        string? Optional(CompiledTemplate? template) => template is null ? null : Text(template) is { Length: > 0 } text ? text : null;

        if (Try(plan, "title", () => Text(templates.Title), out var title))
        {
            plan.Title = title.Length > 0 ? title : null;
            if (plan.Title is null)
            {
                plan.AddError("title: rendered empty");
            }
        }

        if (Try(plan, "season.title", () => Optional(templates.SeasonTitle), out var seasonTitle))
        {
            plan.SeasonTitle = seasonTitle;
        }

        if (Try(plan, "overview", () => Optional(templates.Overview), out var overview))
        {
            plan.Overview = overview;
        }

        if (templates.Absolute is not null && Try(plan, "absolute", () => ToInt(templates.Absolute.Evaluate(row.Model, cancellationToken)), out var absolute))
        {
            plan.AbsoluteEpisodeNumber = absolute;
        }

        if (templates.Runtime is not null && Try(plan, "runtime", () => ToInt(templates.Runtime.Evaluate(row.Model, cancellationToken)), out var runtime))
        {
            plan.Runtime = runtime;
        }

        if (Try(plan, "images.thumb", () => Optional(templates.Thumb), out var thumb))
        {
            plan.Thumb = thumb;
        }

        var terms = (JsonObject)definition.Search.Settings.DeepClone();
        foreach (var (key, list) in templates.Search)
        {
            if (list.Count == 0)
            {
                continue;
            }

            if (!Try(plan, $"search.{key}", () => list.Select(Text).Where(t => t.Length > 0).ToList(), out var rendered))
            {
                return;
            }

            terms[key] = new JsonArray(rendered.Select(t => (JsonNode)t).ToArray());
        }

        plan.SearchTerms = terms;
    }

    private static MappingChange ChangeOf(RowState row)
    {
        var (plan, existing) = (row.Plan, row.Existing);
        if (plan.Errors.Count > 0)
        {
            return MappingChange.Error;
        }

        if (!plan.Kept)
        {
            return existing is null ? MappingChange.Filtered
                : existing.Status == EpisodeStatus.Active ? MappingChange.Orphaned
                : MappingChange.Unchanged;
        }

        if (existing is null)
        {
            return MappingChange.New;
        }

        var same = existing.Status == EpisodeStatus.Active
                   && existing.SeasonNumber == plan.SeasonNumber
                   && existing.EpisodeNumber == plan.EpisodeNumber
                   && existing.AbsoluteEpisodeNumber == plan.AbsoluteEpisodeNumber
                   && existing.Title == plan.Title
                   && existing.Overview == plan.Overview
                   && existing.AirDateUtc == plan.AirDateUtc
                   && existing.AirDate == plan.AirDate
                   && existing.Runtime == plan.Runtime
                   && existing.Images.Thumb == plan.Thumb
                   && existing.NumberLocked == plan.NumberLocked
                   && JsonNode.DeepEquals(existing.SearchTerms, plan.SearchTerms);
        return same ? MappingChange.Unchanged : MappingChange.Changed;
    }

    private static bool Try<T>(MappingPlanRow plan, string location, Func<T> evaluate, out T value)
    {
        try
        {
            value = evaluate();
            return true;
        }
        catch (Exception e) when (e is TemplateException or FormatException)
        {
            plan.AddError($"{location}: {e.Message}");
            value = default!;
            return false;
        }
    }

    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Trim() is { Length: > 0 } text && text != "false" && text != "0",
        int i => i != 0,
        long l => l != 0,
        double d => d != 0,
        decimal d => d != 0,
        _ => true,
    };

    /// <exception cref="FormatException">Not an integer.</exception>
    private static int? ToInt(object? value) => value switch
    {
        null => null,
        int i => i,
        long l and >= int.MinValue and <= int.MaxValue => (int)l,
        double d when d == Math.Floor(d) && Math.Abs(d) <= int.MaxValue => (int)d,
        decimal d when d == decimal.Floor(d) && Math.Abs(d) <= int.MaxValue => (int)d,
        string s when string.IsNullOrWhiteSpace(s) => null,
        string s when int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        _ => throw new FormatException($"'{value}' is not an integer"),
    };

    /// <exception cref="FormatException">Not a date.</exception>
    private static DateTime? ToUtc(object? value, TimeZoneInfo sourceTimezone) => value switch
    {
        null => null,
        DateTime date => FieldValues.ToUtc(date, sourceTimezone),
        DateTimeOffset date => date.UtcDateTime,
        string s when string.IsNullOrWhiteSpace(s) => null,
        string s when FieldValues.TryParseDate(s.Trim(), null, out var date) => FieldValues.ToUtc(date, sourceTimezone),
        _ => throw new FormatException($"'{value}' is not an ISO-8601 date"),
    };

    private sealed class RowState(MappingPlanRow plan, Episode? existing, Dictionary<string, object?> model)
    {
        public MappingPlanRow Plan { get; } = plan;

        public Episode? Existing { get; } = existing;

        public Dictionary<string, object?> Model { get; } = model;

        public int? Season { get; set; }

        public string? Group { get; set; }

        public object? Order { get; set; }

        public int? FieldNumber { get; set; }
    }

    /// <summary>By <c>order</c> (numbers, dates or text; nulls last), then event start and key.</summary>
    private sealed class OrderComparer : IComparer<RowState>
    {
        public static readonly OrderComparer Instance = new();

        public int Compare(RowState? x, RowState? y)
        {
            var result = CompareValues(x!.Order, y!.Order);
            if (result == 0)
            {
                result = Nullable.Compare(x.Plan.Event.Start, y.Plan.Event.Start);
            }

            return result != 0 ? result : string.CompareOrdinal(x.Plan.Event.Key, y.Plan.Event.Key);
        }

        private static int CompareValues(object? x, object? y)
        {
            if (x is null || y is null)
            {
                return (x is null).CompareTo(y is null);
            }

            if (Number(x) is { } a && Number(y) is { } b)
            {
                return a.CompareTo(b);
            }

            if (x is DateTime dx && y is DateTime dy)
            {
                return dx.CompareTo(dy);
            }

            return string.CompareOrdinal(FieldValues.ToText(x), FieldValues.ToText(y));
        }

        private static double? Number(object value) => value switch
        {
            int i => i,
            long l => l,
            double d => d,
            decimal d => (double)d,
            _ => null,
        };
    }

    private sealed class Templates
    {
        public Templates(TemplateRenderer renderer, MappingDefinition definition)
        {
            CompiledTemplate Compile(string text, string location)
            {
                try
                {
                    return renderer.Compile(text);
                }
                catch (TemplateException e)
                {
                    throw new MappingDefinitionException($"{location}: {e.Message}");
                }
            }

            CompiledTemplate? Optional(string? text, string location) => text is null ? null : Compile(text, location);
            List<CompiledTemplate> List(IReadOnlyList<string> texts, string location) => texts.Select((t, i) => Compile(t, $"{location}[{i}]")).ToList();

            Filter = List(definition.Filter, "filter");
            Vars = definition.Vars.Select(v => KeyValuePair.Create(v.Key, Compile(v.Value, $"vars.{v.Key}"))).ToList();
            SeasonValue = definition.Season.Strategy == SeasonStrategy.Field ? Compile(definition.Season.Value!, "season.value") : null;
            SeasonGroup = Optional(definition.Season.Group, "season.group");
            SeasonTitle = Optional(definition.Season.Title, "season.title");
            Order = Compile(definition.Episode.Order, "episode.order");
            EpisodeValue = Optional(definition.Episode.Value, "episode.value");
            Absolute = Optional(definition.Absolute, "absolute");
            Title = Compile(definition.Title, "title");
            Overview = Optional(definition.Overview, "overview");
            AirDate = Compile(definition.AirDate, "airDate");
            Runtime = Optional(definition.Runtime, "runtime");
            Thumb = Optional(definition.Thumb, "images.thumb");
            Search =
            [
                KeyValuePair.Create("queries", List(definition.Search.Queries, "search.queries")),
                KeyValuePair.Create("must", List(definition.Search.Must, "search.must")),
                KeyValuePair.Create("mustNot", List(definition.Search.MustNot, "search.mustNot")),
                KeyValuePair.Create("any", List(definition.Search.Any, "search.any")),
            ];
        }

        public List<CompiledTemplate> Filter { get; }

        public List<KeyValuePair<string, CompiledTemplate>> Vars { get; }

        public CompiledTemplate? SeasonValue { get; }

        public CompiledTemplate? SeasonGroup { get; }

        public CompiledTemplate? SeasonTitle { get; }

        public CompiledTemplate Order { get; }

        public CompiledTemplate? EpisodeValue { get; }

        public CompiledTemplate? Absolute { get; }

        public CompiledTemplate Title { get; }

        public CompiledTemplate? Overview { get; }

        public CompiledTemplate AirDate { get; }

        public CompiledTemplate? Runtime { get; }

        public CompiledTemplate? Thumb { get; }

        public List<KeyValuePair<string, List<CompiledTemplate>>> Search { get; }
    }
}
