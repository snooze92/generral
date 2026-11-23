using System.Text.Json.Nodes;
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.Extensions;
using Generral.Core.Extras.Metadata;
using Generral.Core.HealthCheck;
using Generral.Core.Mappings;
using Generral.Core.Messaging;
using Generral.Core.Notifications;
using Generral.Core.Sources;
using Generral.Core.Sources.Types;
using Generral.Core.Templating;
using Generral.Core.Test.Sources;
using Generral.Core.Tv;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Generral.Core.Test.Mappings;

public class ApplyMappingsServiceTests
{
    private const string Definition = """
        filter: ['{{ event.status != "cancelled" }}']
        season: { strategy: year, title: 'Year {{ season }}' }
        episode: { onInsert: renumber-unlocked }
        title: '{{ event.title }} ({{ source.meta.league }})'
        search: { queries: ['{{ event.title }}'] }
        """;

    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;
    private FakeTime _time = null!;
    private int _mappingId;

    private ApplyMappingsService Apply => _services.GetRequiredService<ApplyMappingsService>();

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _time = new FakeTime(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc));

        var services = new ServiceCollection().AddLogging();
        services.AddDbContext<GenerralDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<TimeProvider>(_time);
        services.AddSourceTypes();
        services.AddSingleton<ProviderStatusTracker>();
        services.AddSingleton<SourceRefreshService>();
        services.AddSingleton<CommandQueue>();
        services.AddSingleton<ICommandHandler, RefreshSourceHandler>();
        services.AddSingleton<ICommandHandler, ApplyMappingsHandler>();
        services.AddSingleton(new MappingEngine(new TemplateRenderer()));
        services.AddScoped<IUiSettingsService, UiSettingsService>();
        services.AddScoped<MetadataService>();
        services.AddSingleton<NotificationProvider>();
        services.AddScoped<NotificationService>();
        services.AddSingleton(sp => new ApplyMappingsService(
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<MappingEngine>(), new ProviderStatusTracker(_time), _time, NullLogger<ApplyMappingsService>.Instance)
        {
            Timezone = TimeZoneInfo.Utc,
        });
        _services = services.BuildServiceProvider();

        using var db = Db();
        db.Database.Migrate();
        var source = new Source { Name = "Fake", Type = "static", Meta = new JsonObject { ["league"] = "UFC" } };
        var series = new Series { Title = "UFC", SortTitle = "ufc", CleanTitle = "ufc", TitleSlug = "ufc", Path = "/tv/UFC", RootFolderPath = "/tv" };
        var mapping = new Mapping { Name = "UFC", Series = series, Source = source, Definition = Definition };
        db.Mappings.Add(mapping);
        db.Events.AddRange(Ev(source, "402", Utc(2026, 6, 1)), Ev(source, "401", Utc(2026, 5, 1)), Ev(source, "399", Utc(2025, 12, 1)));
        db.SaveChanges();
        _mappingId = mapping.Id;
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    [Test]
    public async Task Applying_twice_creates_then_updates_episodes_seasons_links_and_versions()
    {
        var first = await Apply.ApplyAsync(_mappingId);

        using (var db = Db())
        {
            db.Events.Add(Ev(db.Sources.Single(), "400", Utc(2026, 4, 1)));
            db.Events.Single(e => e.Key == "399").Status = EventStatus.Cancelled;
            var mapping = db.Mappings.Single();
            mapping.Definition += "\noverview: Live";
            db.SaveChanges();
        }

        var second = await Apply.ApplyAsync(_mappingId);
        var third = await Apply.ApplyAsync(_mappingId);

        using var check = Db();
        var episodes = check.EpisodeLinks.Include(l => l.Event).Include(l => l.Episode).ToList()
            .OrderBy(l => l.Event!.Key, StringComparer.Ordinal)
            .Select(l => $"{l.Event!.Key} S{l.Episode!.SeasonNumber}E{l.Episode.EpisodeNumber} {l.Episode.Status} {l.Episode.Overview}")
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(first.Summary, Is.EqualTo("UFC: 3 added, 0 changed, 0 unchanged, 0 orphaned, 0 errors"));
            Assert.That(second.Summary, Is.EqualTo("UFC: 1 added, 2 changed, 0 unchanged, 1 orphaned, 0 errors"));
            Assert.That(third.Summary, Is.EqualTo("UFC: 0 added, 0 changed, 4 unchanged, 0 orphaned, 0 errors"));
            Assert.That(episodes, Is.EqualTo(new[]
            {
                "399 S2025E1 Orphaned ",
                "400 S2026E1 Active Live",
                "401 S2026E2 Active Live",
                "402 S2026E3 Active Live",
            }));
            Assert.That(check.Episodes.Single(e => e.EpisodeNumber == 3).Title, Is.EqualTo("402 (UFC)"));
            Assert.That(check.Episodes.Single(e => e.EpisodeNumber == 3).SearchTerms.ToJsonString(), Is.EqualTo("""{"queries":["402"]}"""));
            Assert.That(check.Seasons.OrderBy(s => s.SeasonNumber).Select(s => $"{s.SeasonNumber} {s.Title}"), Is.EqualTo(new[] { "2025 Year 2025", "2026 Year 2026" }));
            Assert.That(check.Mappings.Single().Version, Is.EqualTo(2));
            Assert.That(check.MappingVersions.OrderBy(v => v.Version).Select(v => v.Definition.Contains("overview", StringComparison.Ordinal)), Is.EqualTo(new[] { false, true }));
        });
    }

    [Test]
    public async Task Season_posters_come_from_the_first_event_with_one()
    {
        using (var db = Db())
        {
            db.Mappings.Single().Definition = Definition.Replace(
                "title: 'Year {{ season }}' }",
                "title: 'Year {{ season }}', images: { poster: '{{ if event.key != \"401\" }}https://img.example.org/{{ event.key }}.jpg{{ end }}' } }",
                StringComparison.Ordinal);
            db.SaveChanges();
        }

        await Apply.ApplyAsync(_mappingId);

        using var check = Db();
        Assert.That(check.Seasons.OrderBy(s => s.SeasonNumber).Select(s => $"{s.SeasonNumber} {s.Images.Poster}"), Is.EqualTo(new[]
        {
            "2025 https://img.example.org/399.jpg",
            "2026 https://img.example.org/402.jpg",
        }), "401 is earlier in 2026 but renders empty");
    }

    [Test]
    public async Task Reapplying_keeps_the_monitored_flag_of_existing_episodes()
    {
        await Apply.ApplyAsync(_mappingId);
        using (var db = Db())
        {
            db.Episodes.Single(e => e.Title == "401 (UFC)").Monitored = false;
            db.Events.Single(e => e.Key == "402").Title = "402 renamed";
            db.Mappings.Single().Definition += "\noverview: Live";
            db.SaveChanges();
        }

        var second = await Apply.ApplyAsync(_mappingId);

        using var check = Db();
        Assert.Multiple(() =>
        {
            Assert.That(second.Changed, Is.EqualTo(3));
            Assert.That(check.Episodes.OrderBy(e => e.AirDateUtc).Select(e => $"{e.Title} {e.Monitored}"),
                Is.EqualTo(new[] { "399 (UFC) True", "401 (UFC) False", "402 renamed (UFC) True" }));
        });
    }

    [Test]
    public async Task New_episodes_are_monitored_only_when_the_series_monitors_new_items_and_the_season_is_monitored()
    {
        using (var db = Db())
        {
            db.Series.Single().MonitorNewItems = NewItemMonitorType.None;
            db.SaveChanges();
        }

        await Apply.ApplyAsync(_mappingId);
        using (var db = Db())
        {
            db.Series.Single().MonitorNewItems = NewItemMonitorType.All;
            db.Seasons.Single(s => s.SeasonNumber == 2025).Monitored = true;
            db.Events.Add(Ev(db.Sources.Single(), "398", Utc(2025, 11, 1)));
            db.Events.Add(Ev(db.Sources.Single(), "403", Utc(2026, 7, 1)));
            db.Events.Add(Ev(db.Sources.Single(), "404", Utc(2027, 1, 1)));
            db.SaveChanges();
        }

        await Apply.ApplyAsync(_mappingId);

        using var check = Db();
        Assert.Multiple(() =>
        {
            Assert.That(check.Episodes.OrderBy(e => e.AirDateUtc).Select(e => $"{e.Title} {e.Monitored}"), Is.EqualTo(new[]
            {
                "398 (UFC) True", "399 (UFC) False", "401 (UFC) False", "402 (UFC) False", "403 (UFC) False", "404 (UFC) True",
            }), "398: season monitored since; 403: its season was created unmonitored; 404: new monitored season");
            Assert.That(check.Seasons.OrderBy(s => s.SeasonNumber).Select(s => s.Monitored), Is.EqualTo(new[] { true, false, true }));
        });
    }

    [Test]
    public async Task Successful_source_refresh_enqueues_apply_for_its_mappings()
    {
        var queue = _services.GetRequiredService<CommandQueue>();
        int sourceId;
        using (var db = Db())
        {
            var source = db.Sources.Single();
            source.Settings = new JsonObject { ["events"] = "- { key: '500', title: '500', start: 2026-08-01T20:00:00Z }" };
            db.SaveChanges();
            sourceId = source.Id;
        }

        queue.Enqueue(RefreshSourceHandler.CommandName, new JsonObject { ["sourceId"] = sourceId });
        while (queue.Pending.TryRead(out var command))
        {
            await queue.ExecuteAsync(command, NullLogger.Instance, CancellationToken.None);
        }

        var commands = queue.All();
        Assert.Multiple(() =>
        {
            Assert.That(commands.Select(c => $"{c.Name} {c.Status}"), Is.EqualTo(new[] { "ApplyMappings Completed", "RefreshSource Completed" }), commands[0].Message);
            Assert.That(commands[0].Body.ToJsonString(), Is.EqualTo($$"""{"mappingId":{{_mappingId}}}"""));
            Assert.That(commands[0].Message, Does.StartWith("UFC: 4 added"), "the 3 seeded events are not gone yet");
        });
    }

    private GenerralDbContext Db() => _services.CreateScope().ServiceProvider.GetRequiredService<GenerralDbContext>();

    private static Event Ev(Source source, string key, DateTime start) =>
        new() { Source = source, Key = key, Title = key, Start = start, Hash = key, Status = EventStatus.Scheduled };

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 20, 0, 0, DateTimeKind.Utc);
}
