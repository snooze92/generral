using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Mappings;
using Generral.Core.Sources;
using Generral.Core.Tags;
using Generral.Core.Tv;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Test.Datastore;

public class CatalogueModelTests
{
    private static readonly DateTime Start = new(2026, 11, 15, 3, 0, 0, DateTimeKind.Utc);

    private SqliteConnection _connection = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var db = NewContext();
        db.Database.Migrate();
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    [Test]
    public void Source_and_event_round_trip_with_json_columns()
    {
        using (var db = NewContext())
        {
            var source = NewSource();
            source.Settings = new JsonObject { ["leagueId"] = 4443, ["apiKey"] = "123" };
            source.Tags = [1, 2];
            source.RefreshInterval = TimeSpan.FromHours(6);
            db.Sources.Add(source);
            db.Events.Add(new Event
            {
                Source = source,
                Key = "2190001",
                Title = "UFC 322: Della Maddalena vs Makhachev",
                Start = Start,
                Timezone = "America/New_York",
                Status = EventStatus.Scheduled,
                Fields = new JsonObject { ["strVenue"] = "Madison Square Garden", ["_raw"] = new JsonObject { ["idEvent"] = "2190001" } },
                Images = new ImageSet { Poster = "https://img/poster.jpg" },
                Hash = "abc",
            });
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            var source = db.Sources.Single();
            var ev = db.Events.Single();

            Assert.Multiple(() =>
            {
                Assert.That(source.Settings["leagueId"]!.GetValue<int>(), Is.EqualTo(4443));
                Assert.That(source.Tags, Is.EqualTo(new[] { 1, 2 }));
                Assert.That(source.RefreshInterval, Is.EqualTo(TimeSpan.FromHours(6)));
                Assert.That(source.Enabled, Is.True);
                Assert.That(ev.SourceId, Is.EqualTo(source.Id));
                Assert.That(ev.Start, Is.EqualTo(Start));
                Assert.That(ev.Start!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
                Assert.That(ev.FirstSeen.Kind, Is.EqualTo(DateTimeKind.Utc));
                Assert.That(ev.Status, Is.EqualTo(EventStatus.Scheduled));
                Assert.That(ev.Fields["_raw"]!["idEvent"]!.GetValue<string>(), Is.EqualTo("2190001"));
                Assert.That(ev.Images, Is.EqualTo(new ImageSet { Poster = "https://img/poster.jpg" }));
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(Scalar("SELECT Status FROM Events"), Is.EqualTo("Scheduled"));
            Assert.That(Scalar("SELECT Images FROM Events"), Is.EqualTo("""{"poster":"https://img/poster.jpg"}"""));
            Assert.That(Scalar("SELECT Tags FROM Sources"), Is.EqualTo("[1,2]"));
        });
    }

    [Test]
    public void Changes_inside_json_columns_are_detected()
    {
        using (var db = NewContext())
        {
            db.Sources.Add(NewSource());
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            var source = db.Sources.Single();
            source.Settings["leagueId"] = 4444;
            source.Tags.Add(7);
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            var source = db.Sources.Single();
            Assert.Multiple(() =>
            {
                Assert.That(source.Settings["leagueId"]!.GetValue<int>(), Is.EqualTo(4444));
                Assert.That(source.Tags, Is.EqualTo(new[] { 7 }));
            });
        }
    }

    [Test]
    public void Event_revision_round_trips()
    {
        using (var db = NewContext())
        {
            var ev = NewEvent(NewSource());
            db.EventRevisions.Add(new EventRevision
            {
                Event = ev,
                Title = "UFC 322: TBD vs TBD",
                Start = Start,
                Status = EventStatus.Scheduled,
                Fields = new JsonObject { ["strVenue"] = "TBD" },
                Hash = "old",
            });
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            var revision = db.EventRevisions.Include(r => r.Event).Single();
            Assert.Multiple(() =>
            {
                Assert.That(revision.Event!.Key, Is.EqualTo("e1"));
                Assert.That(revision.Title, Is.EqualTo("UFC 322: TBD vs TBD"));
                Assert.That(revision.Fields["strVenue"]!.GetValue<string>(), Is.EqualTo("TBD"));
                Assert.That(revision.Created.Kind, Is.EqualTo(DateTimeKind.Utc));
            });
        }
    }

    [Test]
    public void Series_season_and_episode_round_trip()
    {
        using (var db = NewContext())
        {
            var series = NewSeries();
            series.Genres = ["Sport", "MMA"];
            series.Tags = [3];
            series.Images = new ImageSet { Poster = "p", Fanart = "f" };
            series.SearchSettings = new JsonObject { ["queries"] = new JsonArray("UFC {{ vars.number }}") };
            series.SeriesType = SeriesType.Daily;
            db.Series.Add(series);
            db.Seasons.Add(new Season { Series = series, SeasonNumber = 2026, Title = "2026", Images = new ImageSet { Poster = "s" } });
            db.Episodes.Add(new Episode
            {
                Series = series,
                SeasonNumber = 2026,
                EpisodeNumber = 1,
                AbsoluteEpisodeNumber = 322,
                Title = "UFC 322",
                AirDateUtc = Start,
                AirDate = new DateOnly(2026, 11, 14),
                Runtime = 300,
                SearchTerms = new JsonObject { ["must"] = new JsonArray(@"\bUFC[ ._-]?322\b") },
                NumberLocked = true,
                Source = EpisodeSource.Manual,
            });
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            var series = db.Series.Single();
            var season = db.Seasons.Single();
            var episode = db.Episodes.Single();

            Assert.Multiple(() =>
            {
                Assert.That(series.Guid, Does.Match("^generral://series/[0-9a-f-]{36}$"));
                Assert.That(series.Genres, Is.EqualTo(new[] { "Sport", "MMA" }));
                Assert.That(series.Tags, Is.EqualTo(new[] { 3 }));
                Assert.That(series.Images, Is.EqualTo(new ImageSet { Poster = "p", Fanart = "f" }));
                Assert.That(series.SearchSettings["queries"]![0]!.GetValue<string>(), Is.EqualTo("UFC {{ vars.number }}"));
                Assert.That(series.SeriesType, Is.EqualTo(SeriesType.Daily));
                Assert.That(series.MonitorNewItems, Is.EqualTo(NewItemMonitorType.All));
                Assert.That(series.Added.Kind, Is.EqualTo(DateTimeKind.Utc));
                Assert.That(season.SeriesId, Is.EqualTo(series.Id));
                Assert.That(season.Title, Is.EqualTo("2026"));
                Assert.That(season.Images.Poster, Is.EqualTo("s"));
                Assert.That(episode.AbsoluteEpisodeNumber, Is.EqualTo(322));
                Assert.That(episode.AirDateUtc, Is.EqualTo(Start));
                Assert.That(episode.AirDate, Is.EqualTo(new DateOnly(2026, 11, 14)));
                Assert.That(episode.SearchTerms["must"]![0]!.GetValue<string>(), Is.EqualTo(@"\bUFC[ ._-]?322\b"));
                Assert.That(episode.NumberLocked, Is.True);
                Assert.That(episode.HasFile, Is.False);
                Assert.That(episode.Source, Is.EqualTo(EpisodeSource.Manual));
                Assert.That(episode.Status, Is.EqualTo(EpisodeStatus.Active));
            });
        }

        Assert.That(Scalar("SELECT SeriesType FROM Series"), Is.EqualTo("Daily"));
    }

    [Test]
    public void New_series_get_distinct_guids()
    {
        var first = NewSeries();
        var second = NewSeries();

        Assert.That(first.Guid, Is.Not.EqualTo(second.Guid));
    }

    [Test]
    public void Mapping_and_versions_round_trip()
    {
        const string definition = "season: { strategy: year }";
        using (var db = NewContext())
        {
            var mapping = new Mapping { Name = "UFC", Series = NewSeries(), Source = NewSource(), Priority = 1, Definition = definition, Version = 2 };
            db.Mappings.Add(mapping);
            db.MappingVersions.Add(new MappingVersion { Mapping = mapping, Version = 1, Definition = "" });
            db.MappingVersions.Add(new MappingVersion { Mapping = mapping, Version = 2, Definition = definition });
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            var mapping = db.Mappings.Single();
            var versions = db.MappingVersions.OrderBy(v => v.Version).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(mapping.Definition, Is.EqualTo(definition));
                Assert.That(mapping.Priority, Is.EqualTo(1));
                Assert.That(mapping.Version, Is.EqualTo(2));
                Assert.That(versions.Select(v => v.Version), Is.EqualTo(new[] { 1, 2 }));
                Assert.That(versions[1].Definition, Is.EqualTo(definition));
            });

            db.MappingVersions.Add(new MappingVersion { MappingId = mapping.Id, Version = 2 });
            Assert.That(db.SaveChanges, Throws.InstanceOf<DbUpdateException>());
        }
    }

    [Test]
    public void Tag_round_trips_and_label_is_unique_ignoring_case()
    {
        using (var db = NewContext())
        {
            db.Tags.Add(new Tag { Label = "sport" });
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            Assert.That(db.Tags.Single().Label, Is.EqualTo("sport"));
            db.Tags.Add(new Tag { Label = "SPORT" });
            Assert.That(db.SaveChanges, Throws.InstanceOf<DbUpdateException>());
        }
    }

    [Test]
    public void Source_name_is_unique()
    {
        AssertSecondInsertFails(db => db.Sources.Add(NewSource()));
    }

    [Test]
    public void Event_key_is_unique_per_source()
    {
        int sourceId;
        using (var db = NewContext())
        {
            var other = NewSource();
            other.Name = "Other";
            db.Sources.Add(other);
            var source = NewSource();
            db.Events.Add(NewEvent(source));
            db.SaveChanges();
            sourceId = source.Id;

            db.Events.Add(NewEvent(other));
            Assert.That(db.SaveChanges, Throws.Nothing);
        }

        AssertSecondInsertFails(db => db.Events.Add(new Event { SourceId = sourceId, Key = "e1", Title = "Dup", Hash = "x" }), insertFirst: false);
    }

    [Test]
    public void Season_number_is_unique_per_series()
    {
        var seriesId = AddSeries();
        AssertSecondInsertFails(db => db.Seasons.Add(new Season { SeriesId = seriesId, SeasonNumber = 1 }));
    }

    [Test]
    public void Episode_number_is_unique_per_season()
    {
        var seriesId = AddSeries();
        using (var db = NewContext())
        {
            db.Episodes.Add(new Episode { SeriesId = seriesId, SeasonNumber = 1, EpisodeNumber = 1, Title = "a" });
            db.Episodes.Add(new Episode { SeriesId = seriesId, SeasonNumber = 2, EpisodeNumber = 1, Title = "b" });
            db.SaveChanges();
        }

        AssertSecondInsertFails(db => db.Episodes.Add(new Episode { SeriesId = seriesId, SeasonNumber = 1, EpisodeNumber = 1, Title = "c" }), insertFirst: false);
    }

    [Test]
    public void Episode_links_are_unique_with_and_without_part()
    {
        int mappingId, eventId, episode1, episode2, episode3;
        using (var db = NewContext())
        {
            var series = NewSeries();
            var source = NewSource();
            var mapping = new Mapping { Name = "m", Series = series, Source = source };
            var ev = NewEvent(source);
            var episodes = Enumerable.Range(1, 3).Select(n => new Episode { Series = series, SeasonNumber = 1, EpisodeNumber = n, Title = $"e{n}" }).ToList();
            db.AddRange(mapping, ev);
            db.Episodes.AddRange(episodes);
            db.SaveChanges();
            (mappingId, eventId) = (mapping.Id, ev.Id);
            (episode1, episode2, episode3) = (episodes[0].Id, episodes[1].Id, episodes[2].Id);

            db.EpisodeLinks.Add(new EpisodeLink { MappingId = mappingId, EventId = eventId, EpisodeId = episode1 });
            db.EpisodeLinks.Add(new EpisodeLink { MappingId = mappingId, EventId = eventId, PartKey = "main-card", EpisodeId = episode2 });
            db.EpisodeLinks.Add(new EpisodeLink { MappingId = mappingId, EventId = eventId, PartKey = "prelims", EpisodeId = episode3 });
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            var links = db.EpisodeLinks.OrderBy(l => l.EpisodeId).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(links.Select(l => l.PartKey), Is.EqualTo(new[] { EpisodeLink.NoPart, "main-card", "prelims" }));
                Assert.That(links[0].CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            });
        }

        AssertSecondInsertFails(db => db.EpisodeLinks.Add(new EpisodeLink { MappingId = mappingId, EventId = eventId, EpisodeId = episode3 }), insertFirst: false);
        AssertSecondInsertFails(db => db.EpisodeLinks.Add(new EpisodeLink { MappingId = mappingId, EventId = eventId, PartKey = "main-card", EpisodeId = episode1 }), insertFirst: false);
    }

    [Test]
    public void Deleting_a_source_referenced_by_a_mapping_is_restricted()
    {
        int sourceId;
        using (var db = NewContext())
        {
            var source = NewSource();
            db.Mappings.Add(new Mapping { Name = "m", Series = NewSeries(), Source = source });
            db.SaveChanges();
            sourceId = source.Id;
        }

        using (var db = NewContext())
        {
            db.Sources.Remove(db.Sources.Single());
            Assert.That(db.SaveChanges, Throws.InstanceOf<DbUpdateException>());
        }

        using (var db = NewContext())
        {
            db.Mappings.Remove(db.Mappings.Single());
            db.SaveChanges();
            db.Sources.Remove(db.Sources.Single(s => s.Id == sourceId));
            Assert.That(db.SaveChanges, Throws.Nothing);
        }
    }

    [Test]
    public void Deleting_a_series_cascades_to_its_catalogue()
    {
        using (var db = NewContext())
        {
            var series = NewSeries();
            var source = NewSource();
            var mapping = new Mapping { Name = "m", Series = series, Source = source };
            var episode = new Episode { Series = series, SeasonNumber = 1, EpisodeNumber = 1, Title = "e" };
            db.Seasons.Add(new Season { Series = series, SeasonNumber = 1 });
            db.MappingVersions.Add(new MappingVersion { Mapping = mapping, Version = 1 });
            db.EpisodeLinks.Add(new EpisodeLink { Mapping = mapping, Event = NewEvent(source), Episode = episode });
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            db.Series.Where(s => true).ExecuteDelete();

            Assert.Multiple(() =>
            {
                Assert.That(db.Seasons.Count(), Is.Zero);
                Assert.That(db.Episodes.Count(), Is.Zero);
                Assert.That(db.Mappings.Count(), Is.Zero);
                Assert.That(db.MappingVersions.Count(), Is.Zero);
                Assert.That(db.EpisodeLinks.Count(), Is.Zero);
                Assert.That(db.Events.Count(), Is.EqualTo(1));
            });
        }
    }

    private GenerralDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GenerralDbContext>().UseSqlite(_connection).Options);

    private object? Scalar(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private int AddSeries()
    {
        using var db = NewContext();
        var series = NewSeries();
        db.Series.Add(series);
        db.SaveChanges();
        return series.Id;
    }

    private void AssertSecondInsertFails(Action<GenerralDbContext> insert, bool insertFirst = true)
    {
        if (insertFirst)
        {
            using var first = NewContext();
            insert(first);
            first.SaveChanges();
        }

        using var db = NewContext();
        insert(db);
        Assert.That(db.SaveChanges, Throws.InstanceOf<DbUpdateException>().With.InnerException.Message.Contains("UNIQUE"));
    }

    private static Source NewSource() => new() { Name = "TheSportsDB – UFC", Type = "thesportsdb" };

    private static Event NewEvent(Source source) => new() { Source = source, Key = "e1", Title = "UFC 322", Hash = "h" };

    private static Series NewSeries() => new()
    {
        Title = "UFC",
        SortTitle = "ufc",
        CleanTitle = "ufc",
        TitleSlug = $"ufc-{Guid.NewGuid():N}",
        Path = "/tv/UFC",
        RootFolderPath = "/tv",
    };
}
