using System.Text.Json.Nodes;
using Generral.Core.Mappings;
using Generral.Core.Sources;
using Generral.Core.Templating;
using Generral.Core.Tv;

namespace Generral.Core.Test.Mappings;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class MappingEngineTests
{
    private const string Ufc = """
        filter:
          - '{{ event.title | regex.match "^UFC \\d+\\b" }}'
          - '{{ event.status != "cancelled" }}'
        vars:
          number:   '{{ (event.title | regex.match "^UFC (\\d+)")[1] | string.to_int }}'
          headline: '{{ event.title | regex.replace "^UFC \\d+:?\\s*" "" }}'
        season:  { strategy: year }
        episode: { strategy: sequential, order: '{{ event.start }}', onInsert: ONINSERT }
        absolute: '{{ vars.number }}'
        title:    'UFC {{ vars.number }}{{ if vars.headline != "" }}: {{ vars.headline }}{{ end }}'
        overview: '{{ [event.fields.venue, event.fields.city] | array.compact | array.join ", " }}'
        runtime:  180
        search:
          categories: [5000]
          queries: ['UFC {{ vars.number }} S{{ season }}E{{ episode }}']
          mustNot: ['\bfight ?night\b']
        """;

    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly MappingEngine Engine = new(new TemplateRenderer());

    private readonly Source _source = new() { Id = 1, Name = "Fake", Type = "static" };
    private readonly Series _series = new() { Id = 1, Title = "UFC", SortTitle = "ufc", CleanTitle = "ufc", TitleSlug = "ufc", Path = "/tv/UFC", RootFolderPath = "/tv" };
    private readonly List<Event> _events = [];
    private readonly List<Episode> _episodes = [];
    private readonly List<EpisodeLink> _links = [];
    private readonly List<DedupeClaim> _claims = [];

    [Test]
    public void Ufc_events_get_year_seasons_and_sequential_numbers()
    {
        AddEvent("314", "UFC 314: Volkanovski vs. Lopes", Utc(2025, 4, 12, 22), new JsonObject { ["venue"] = "Kaseya Center", ["city"] = "Miami" });
        AddEvent("fn", "UFC Fight Night: Edwards vs. Brady", Utc(2025, 3, 22, 18));
        AddEvent("313", "UFC 313", Utc(2025, 3, 8, 22));
        AddEvent("320", "UFC 320", Utc(2026, 4, 4, 22));
        AddEvent("319", "UFC 319", Utc(2025, 12, 13, 22)).Status = EventStatus.Cancelled;

        var plan = Run(Ufc);

        Assert.Multiple(() =>
        {
            Assert.That(Numbers(), Is.EqualTo(new[] { "313 S2025E01", "314 S2025E02", "320 S2026E01" }));
            Assert.That(Row(plan, "fn").RejectedBy, Does.Contain("regex.match"));
            Assert.That(Row(plan, "319").RejectedBy, Does.Contain("cancelled"));
            Assert.That(Row(plan, "fn").Change, Is.EqualTo(MappingChange.Filtered));
            var e314 = Episode("314");
            Assert.That(e314.Title, Is.EqualTo("UFC 314: Volkanovski vs. Lopes"));
            Assert.That(e314.AbsoluteEpisodeNumber, Is.EqualTo(314));
            Assert.That(e314.Overview, Is.EqualTo("Kaseya Center, Miami"));
            Assert.That(e314.Runtime, Is.EqualTo(180));
            Assert.That(e314.NumberLocked, Is.True, "aired before now");
            Assert.That(Episode("320").NumberLocked, Is.False);
            Assert.That(e314.SearchTerms.ToJsonString(), Is.EqualTo("""{"categories":[5000],"queries":["UFC 314 S2025E2"],"mustNot":["\\bfight ?night\\b"]}"""));
            Assert.That(Episode("313").Title, Is.EqualTo("UFC 313"));
            Assert.That(plan.Seasons.Keys, Is.EquivalentTo(new[] { 2025, 2026 }));
        });
    }

    [Test]
    public void Rerunning_is_stable_and_updates_metadata_without_renumbering()
    {
        AddEvent("401", "UFC 401", Utc(2026, 5, 1));
        AddEvent("402", "UFC 402", Utc(2026, 6, 1));
        Run(Ufc);

        var rerun = Run(Ufc);
        _events[1].Title = "UFC 402: Jones vs. Aspinall";
        _events[1].Start = Utc(2026, 4, 1);
        var changed = Run(Ufc);

        Assert.Multiple(() =>
        {
            Assert.That(rerun.Rows.Select(r => r.Change), Is.All.EqualTo(MappingChange.Unchanged));
            Assert.That(Row(changed, "401").Change, Is.EqualTo(MappingChange.Unchanged));
            Assert.That(Row(changed, "402").Change, Is.EqualTo(MappingChange.Changed));
            Assert.That(Numbers(), Is.EqualTo(new[] { "401 S2026E01", "402 S2026E02" }), "append keeps numbers even when the order changes");
            Assert.That(Episode("402").Title, Is.EqualTo("UFC 402: Jones vs. Aspinall"));
        });
    }

    [Test]
    public void Episode_poster_is_mapped_and_a_new_poster_is_a_change()
    {
        var yaml = Ufc + "\n" + """
            images:
              thumb:  '{{ event.images.thumb }}'
              poster: '{{ event.images.poster }}'
            """;
        AddEvent("401", "UFC 401", Utc(2026, 5, 1)).Images = new() { Poster = "https://img/401.jpg" };
        Run(yaml);
        var first = Episode("401").Images;
        _events.Single().Images = new() { Poster = "https://img/401-v2.jpg" };

        var plan = Run(yaml, apply: false);

        Assert.Multiple(() =>
        {
            Assert.That(first.Poster, Is.EqualTo("https://img/401.jpg"));
            Assert.That(first.Thumb, Is.Null);
            Assert.That(Row(plan, "401").Poster, Is.EqualTo("https://img/401-v2.jpg"));
            Assert.That(Row(plan, "401").Change, Is.EqualTo(MappingChange.Changed));
        });
    }

    [Test]
    public void Inserted_event_is_appended()
    {
        AddEvent("401", "UFC 401", Utc(2026, 5, 1));
        AddEvent("403", "UFC 403", Utc(2026, 7, 1));
        Run(Ufc);
        AddEvent("402", "UFC 402", Utc(2026, 6, 1));

        var plan = Run(Ufc);

        Assert.Multiple(() =>
        {
            Assert.That(Numbers(), Is.EqualTo(new[] { "401 S2026E01", "402 S2026E03", "403 S2026E02" }));
            Assert.That(Row(plan, "402").Change, Is.EqualTo(MappingChange.New));
        });
    }

    [Test]
    public void Inserted_event_renumbers_unlocked_episodes()
    {
        AddEvent("401", "UFC 401", Utc(2026, 5, 1));
        AddEvent("403", "UFC 403", Utc(2026, 7, 1));
        Run(Ufc, InsertPolicy.RenumberUnlocked);
        AddEvent("402", "UFC 402", Utc(2026, 6, 1));

        var plan = Run(Ufc, InsertPolicy.RenumberUnlocked);

        Assert.Multiple(() =>
        {
            Assert.That(Numbers(), Is.EqualTo(new[] { "401 S2026E01", "402 S2026E02", "403 S2026E03" }));
            Assert.That(Row(plan, "401").Change, Is.EqualTo(MappingChange.Unchanged));
            Assert.That(Row(plan, "403").Change, Is.EqualTo(MappingChange.Changed));
        });
    }

    [Test]
    public void Locked_episodes_keep_their_numbers_when_renumbering()
    {
        AddEvent("398", "UFC 398", Utc(2026, 2, 1));
        AddEvent("401", "UFC 401", Utc(2026, 5, 1));
        AddEvent("403", "UFC 403", Utc(2026, 7, 1));
        Run(Ufc, InsertPolicy.RenumberUnlocked);
        Episode("403").NumberLocked = true;
        AddEvent("397", "UFC 397", Utc(2026, 1, 1));
        AddEvent("402", "UFC 402", Utc(2026, 6, 1));

        Run(Ufc, InsertPolicy.RenumberUnlocked);

        Assert.That(Numbers(), Is.EqualTo(new[] { "397 S2026E02", "398 S2026E01", "401 S2026E04", "402 S2026E05", "403 S2026E03" }),
            "398 aired and 403 is locked: the others take the free numbers in order");
    }

    [Test]
    public void Episodes_with_a_file_keep_their_numbers_when_renumbering_even_without_the_lock_flag()
    {
        AddEvent("398", "UFC 398", Utc(2026, 2, 1));
        AddEvent("401", "UFC 401", Utc(2026, 5, 1));
        AddEvent("403", "UFC 403", Utc(2026, 7, 1));
        Run(Ufc, InsertPolicy.RenumberUnlocked);
        Episode("403").EpisodeFileId = 1;
        AddEvent("397", "UFC 397", Utc(2026, 1, 1));
        AddEvent("402", "UFC 402", Utc(2026, 6, 1));

        Run(Ufc, InsertPolicy.RenumberUnlocked);

        Assert.Multiple(() =>
        {
            Assert.That(Numbers(), Is.EqualTo(new[] { "397 S2026E02", "398 S2026E01", "401 S2026E04", "402 S2026E05", "403 S2026E03" }));
            Assert.That(Episode("403").NumberLocked, Is.True);
        });
    }

    [Test]
    public void Filtered_out_and_gone_events_orphan_their_episode_and_keep_the_number()
    {
        AddEvent("401", "UFC 401", Utc(2026, 5, 1));
        AddEvent("402", "UFC 402", Utc(2026, 6, 1));
        AddEvent("403", "UFC 403", Utc(2026, 7, 1));
        Run(Ufc, InsertPolicy.RenumberUnlocked);
        _events[0].Status = EventStatus.Cancelled;
        _events[1].Status = EventStatus.Gone;
        AddEvent("404", "UFC 404", Utc(2026, 4, 1));

        var plan = Run(Ufc, InsertPolicy.RenumberUnlocked);

        Assert.Multiple(() =>
        {
            Assert.That(Row(plan, "401").Change, Is.EqualTo(MappingChange.Orphaned));
            Assert.That(Row(plan, "402").RejectedBy, Is.EqualTo(MappingEngine.GoneReason));
            Assert.That(Episode("401").Status, Is.EqualTo(EpisodeStatus.Orphaned));
            Assert.That(Numbers(), Is.EqualTo(new[] { "401 S2026E01", "402 S2026E02", "403 S2026E04", "404 S2026E03" }), "orphaned numbers are not reused");
            Assert.That(Run(Ufc, InsertPolicy.RenumberUnlocked).Rows.Select(r => r.Change), Is.All.EqualTo(MappingChange.Unchanged));
        });
    }

    [Test]
    public void Field_numbers_that_collide_are_row_errors()
    {
        const string definition = """
            season: { strategy: fixed, value: 1 }
            episode: { strategy: field, value: '{{ event.fields.round }}' }
            """;
        AddEvent("a", "Round 1", Utc(2026, 5, 1), new JsonObject { ["round"] = 1 });
        AddEvent("b", "Round 2", Utc(2026, 5, 8), new JsonObject { ["round"] = "2" });
        Run(definition);
        AddEvent("c", "Round 2 again", Utc(2026, 5, 9), new JsonObject { ["round"] = 2 });

        var plan = Run(definition);

        Assert.Multiple(() =>
        {
            Assert.That(Row(plan, "c").Change, Is.EqualTo(MappingChange.Error));
            Assert.That(Row(plan, "c").Errors.Single(), Is.EqualTo("episode.value: S01E02 is already used by episode 'Round 2'"));
            Assert.That(Numbers(), Is.EqualTo(new[] { "a S01E01", "b S01E02" }));
        });
    }

    [Test]
    public void Year_seasons_use_the_local_date_of_the_series_timezone()
    {
        AddEvent("new-year", "UFC 400", Utc(2027, 1, 1, 3));

        var newYork = Run(Ufc, timezone: TimeZoneInfo.FindSystemTimeZoneById("America/New_York"), apply: false);
        var row = Row(newYork, "new-year");
        var utc = Row(Run(Ufc, timezone: TimeZoneInfo.Utc, apply: false), "new-year");

        Assert.Multiple(() =>
        {
            Assert.That(row.AirDateUtc, Is.EqualTo(Utc(2027, 1, 1, 3)));
            Assert.That(row.AirDate, Is.EqualTo(new DateOnly(2026, 12, 31)));
            Assert.That(row.SeasonNumber, Is.EqualTo(2026));
            Assert.That(utc.SeasonNumber, Is.EqualTo(2027));
        });
    }

    [Test]
    public void Air_dates_without_offset_use_the_source_timezone()
    {
        _source.Settings = new JsonObject { ["timezone"] = "Europe/Paris" };
        AddEvent("a", "UFC 400", Utc(2026, 5, 1)).Fields = new JsonObject { ["local"] = "2026-07-01 21:00" };

        var row = Row(Run("airDate: '{{ event.fields.local }}'", apply: false), "a");

        Assert.That(row.AirDateUtc, Is.EqualTo(Utc(2026, 7, 1, 19)));
    }

    [Test]
    public void Template_errors_only_affect_their_row()
    {
        AddEvent("401", "UFC 401", Utc(2026, 5, 1), new JsonObject { ["offset"] = "-2h" });
        AddEvent("402", "UFC 402", Utc(2026, 6, 1), new JsonObject { ["offset"] = "soon" });
        AddEvent("403", "UFC 403", Utc(2026, 7, 1), new JsonObject { ["offset"] = "1d" });

        var plan = Run(Ufc.Replace("runtime:  180", "airDate: '{{ event.start | date.add event.fields.offset }}'", StringComparison.Ordinal));
        _events[0].Fields = new JsonObject { ["offset"] = "-2h", ["bad"] = "soon" };
        var filterError = Run(Ufc.Replace("filter:", "filter:\n  - '{{ (event.start | date.add event.fields.bad) != null }}'", StringComparison.Ordinal), apply: false);

        Assert.Multiple(() =>
        {
            Assert.That(Row(filterError, "401").Change, Is.EqualTo(MappingChange.Error), "a failing filter does not orphan the episode");
            Assert.That(Row(filterError, "401").Errors.Single(), Does.StartWith("filter: "));
            Assert.That(Row(plan, "402").Change, Is.EqualTo(MappingChange.Error));
            Assert.That(Row(plan, "402").Errors.Single(), Does.StartWith("airDate: ").And.Contains("Invalid duration 'soon'"));
            Assert.That(Numbers(), Is.EqualTo(new[] { "401 S2026E01", "403 S2026E02" }));
            Assert.That(Episode("401").AirDateUtc, Is.EqualTo(Utc(2026, 4, 30, 22)));
        });
    }

    [Test]
    public void Sequential_seasons_number_groups_by_first_appearance()
    {
        const string definition = """
            season: { strategy: sequential, group: '{{ event.fields.cup }}', title: '{{ event.fields.cup }}' }
            """;
        AddEvent("b1", "B final", Utc(2026, 6, 2), new JsonObject { ["cup"] = "Cup B" });
        AddEvent("a1", "A opener", Utc(2026, 5, 1), new JsonObject { ["cup"] = "Cup A" });
        AddEvent("a2", "A final", Utc(2026, 6, 1), new JsonObject { ["cup"] = "Cup A" });
        Run(definition);
        AddEvent("c1", "C opener", Utc(2026, 1, 1), new JsonObject { ["cup"] = "Cup C" });

        var plan = Run(definition);

        Assert.Multiple(() =>
        {
            Assert.That(Numbers(), Is.EqualTo(new[] { "a1 S01E01", "a2 S01E02", "b1 S02E01", "c1 S03E01" }));
            Assert.That(plan.Seasons, Is.EquivalentTo(new Dictionary<int, string?> { [1] = "Cup A", [2] = "Cup B", [3] = "Cup C" }));
        });
    }

    [Test]
    public void Date_strategy_makes_the_series_daily()
    {
        AddEvent("a", "Show", Utc(2026, 5, 1));

        var plan = Run("episode: { strategy: date }");

        Assert.Multiple(() =>
        {
            Assert.That(plan.SeriesType, Is.EqualTo(SeriesType.Daily));
            Assert.That(Numbers(), Is.EqualTo(new[] { "a S2026E01" }));
        });
    }

    [Test]
    public void Parts_expand_an_event_into_episodes_with_their_own_dates_monitoring_and_search()
    {
        const string parts = """
            vars: { number: '{{ (event.title | regex.match "^UFC (\\d+)")[1] }}' }
            parts:
              - { key: early-prelims, title: Early Prelims, monitored: false, airOffset: -3h, when: '{{ event.fields.early }}' }
              - { key: prelims, title: Prelims, monitored: false, airOffset: -2h, runtime: 120 }
              - { key: main-card, title: Main Card, monitored: true }
            title: 'UFC {{ vars.number }} - {{ part.title }}'
            airDate: '{{ event.start | date.add part.airOffset }}'
            runtime: 180
            search:
              must: ['\bUFC ?{{ vars.number }}\b']
              parts:
                prelims: { must: ['\bprelims?\b'] }
                main-card: { mustNot: ['\bprelims?\b'] }
            """;
        AddEvent("401", "UFC 401", Utc(2026, 5, 2, 22), new JsonObject { ["early"] = true });
        AddEvent("402", "UFC 402", Utc(2026, 6, 6, 22), new JsonObject { ["early"] = false });

        var plan = Run(parts);
        var rerun = Run(parts);

        var prelims = _episodes.Single(e => e.Title == "UFC 401 - Prelims");
        Assert.Multiple(() =>
        {
            Assert.That(_episodes.OrderBy(e => e.EpisodeNumber).Select(e => $"E{e.EpisodeNumber} {e.Title} {e.Monitored}"), Is.EqualTo(new[]
            {
                "E1 UFC 401 - Early Prelims False",
                "E2 UFC 401 - Prelims False",
                "E3 UFC 401 - Main Card True",
                "E4 UFC 402 - Prelims False",
                "E5 UFC 402 - Main Card True",
            }));
            Assert.That(_links.Select(l => l.PartKey), Has.Member("main-card"));
            Assert.That(plan.Rows.Where(r => r.Event.Key == "402").Select(r => r.PartKey), Is.EqualTo(new[] { "prelims", "main-card" }), "excluded parts have no row");
            Assert.That(prelims.AirDateUtc, Is.EqualTo(Utc(2026, 5, 2, 20)));
            Assert.That(prelims.Runtime, Is.EqualTo(120));
            Assert.That(_episodes.Single(e => e.Title == "UFC 401 - Main Card").Runtime, Is.EqualTo(180));
            Assert.That(prelims.SearchTerms.ToJsonString(), Is.EqualTo("""{"must":["\\bUFC ?401\\b","\\bprelims?\\b"]}"""));
            Assert.That(plan.Rows.First(r => r.Event.Key == "401").PartTitle, Is.EqualTo("Early Prelims"));
            Assert.That(rerun.Rows.Select(r => r.Change), Is.All.EqualTo(MappingChange.Unchanged));
        });
    }

    [Test]
    public void Dedupe_skips_events_kept_by_a_higher_priority_mapping_and_takes_over_lower_priority_episodes()
    {
        AddEvent("a", "UFC 401", Utc(2026, 5, 1));
        AddEvent("b", "UFC 402", Utc(2026, 6, 1));
        _episodes.Add(new Episode { Id = 50, SeriesId = _series.Id, SeasonNumber = 2026, EpisodeNumber = 7, Title = "old" });
        _claims.Add(new DedupeClaim("401", EpisodeLink.NoPart, "Primary", true, null));
        _claims.Add(new DedupeClaim("402", EpisodeLink.NoPart, "Patch", false, 50));

        var plan = Run("dedupe: '{{ event.title | string.remove \"UFC \" }}'", apply: false);

        Assert.Multiple(() =>
        {
            Assert.That(Row(plan, "a").Change, Is.EqualTo(MappingChange.Duplicate));
            Assert.That(Row(plan, "a").DuplicateOf, Is.EqualTo("Primary"));
            Assert.That(Row(plan, "b").Change, Is.EqualTo(MappingChange.Changed));
            Assert.That(Row(plan, "b").EpisodeId, Is.EqualTo(50));
            Assert.That(Row(plan, "b").IsLinked, Is.False);
            Assert.That(Row(plan, "b").EpisodeNumber, Is.EqualTo(7), "the taken-over episode keeps its number");
        });
    }

    private MappingPlan Run(string yaml, InsertPolicy onInsert = InsertPolicy.Append, TimeZoneInfo? timezone = null, bool apply = true)
    {
        var definition = MappingDefinitionParser.FromYaml(yaml.Replace("ONINSERT", onInsert == InsertPolicy.Append ? "append" : "renumber-unlocked", StringComparison.Ordinal));
        var plan = Engine.Evaluate(new MappingInput
        {
            Definition = definition,
            Series = _series,
            Source = _source,
            Events = _events,
            Episodes = _episodes,
            Links = _links,
            Claims = _claims,
            Now = Now,
            Timezone = timezone ?? TimeZoneInfo.Utc,
        });

        if (apply)
        {
            Apply(plan);
        }

        return plan;
    }

    /// <summary>What ApplyMappingsService does, in memory.</summary>
    private void Apply(MappingPlan plan)
    {
        foreach (var row in plan.Rows)
        {
            var existing = _episodes.SingleOrDefault(e => e.Id == row.EpisodeId);
            if (row.IsApplied)
            {
                if (existing is null)
                {
                    existing = new Episode { Id = _episodes.Count + 1, SeriesId = _series.Id, Title = "", Monitored = row.Monitored ?? true };
                    _episodes.Add(existing);
                    _links.Add(new EpisodeLink { EventId = row.Event.Id, PartKey = row.LinkPartKey, EpisodeId = existing.Id });
                }

                row.ApplyTo(existing);
            }
            else if (row.Change == MappingChange.Orphaned)
            {
                existing!.Status = EpisodeStatus.Orphaned;
            }
        }

        Assert.That(_episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber)), Is.Unique);
    }

    private Event AddEvent(string key, string title, DateTime start, JsonObject? fields = null)
    {
        var ev = new Event { Id = _events.Count + 1, SourceId = _source.Id, Key = key, Title = title, Start = start, Hash = "", Status = EventStatus.Scheduled, Fields = fields ?? [] };
        _events.Add(ev);
        return ev;
    }

    private Episode Episode(string key)
    {
        var ev = _events.Single(e => e.Key == key);
        return _episodes.Single(e => e.Id == _links.Single(l => l.EventId == ev.Id).EpisodeId);
    }

    private List<string> Numbers() => _links
        .Select(l => (Key: _events.Single(e => e.Id == l.EventId).Key, Episode: _episodes.Single(e => e.Id == l.EpisodeId)))
        .OrderBy(x => x.Key, StringComparer.Ordinal)
        .Select(x => $"{x.Key} S{x.Episode.SeasonNumber:00}E{x.Episode.EpisodeNumber:00}")
        .ToList();

    private static MappingPlanRow Row(MappingPlan plan, string key) => plan.Rows.Single(r => r.Event.Key == key);

    private static DateTime Utc(int year, int month, int day, int hour = 0) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
}
