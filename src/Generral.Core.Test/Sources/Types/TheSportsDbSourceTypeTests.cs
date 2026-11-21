using System.Text.Json.Nodes;
using Generral.Core.Sources;
using Generral.Core.Sources.Types;

namespace Generral.Core.Test.Sources.Types;

public class TheSportsDbSourceTypeTests
{
    private const string Api = "https://www.thesportsdb.com/api/v1/json/123/";

    private SourceTestHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new SourceTestHost();
        _host.Handler
            .Add(Api + "lookupleague.php?id=4443", SourceTestHost.Fixture("thesportsdb/lookupleague.json"))
            .Add(Api + "search_all_seasons.php?id=4443", SourceTestHost.Fixture("thesportsdb/search_all_seasons.json"))
            .Add(Api + "eventsseason.php?id=4443&s=2026", SourceTestHost.Fixture("thesportsdb/eventsseason-2026.json"))
            .Add(Api + "eventspastleague.php?id=4443", SourceTestHost.Fixture("thesportsdb/eventspastleague.json"))
            .Add(Api + "eventsnextleague.php?id=4443", SourceTestHost.Fixture("thesportsdb/eventsnextleague.json"));
        foreach (var season in new[] { "2022", "2023", "2024", "2025" })
        {
            _host.Handler.Add(Api + $"eventsseason.php?id=4443&s={season}", """{"events": null}""");
        }
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public async Task Fetches_the_current_season_topped_up_with_past_and_next_events()
    {
        var result = await _host.FetchAsync("thesportsdb", new JsonObject { ["leagueId"] = "4443" });

        var events = result.Events.ToDictionary(e => e.Key);
        Assert.Multiple(() =>
        {
            Assert.That(_host.Handler.RequestedUrls, Is.EqualTo(new[]
            {
                Api + "lookupleague.php?id=4443",
                Api + "eventsseason.php?id=4443&s=2026",
                Api + "eventspastleague.php?id=4443",
                Api + "eventsnextleague.php?id=4443",
            }));
            Assert.That(events.Keys, Is.EqualTo(new[] { "2001", "2002", "2003", "1999", "2004" }));
            Assert.That(events["2001"].Title, Is.EqualTo("UFC 330: Alpha vs Beta"));
            Assert.That(events["2001"].Start, Is.EqualTo(new DateTime(2026, 4, 11, 22, 0, 0, DateTimeKind.Utc)));
            Assert.That(events["2001"].Start!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(events["2001"].Status, Is.EqualTo(EventStatus.Completed));
            Assert.That(events["2001"].Images.Poster, Is.EqualTo("https://r2.thesportsdb.com/images/media/event/poster/330.jpg"));
            Assert.That(events["2001"].Images.Thumb, Does.EndWith("/thumb/330.jpg"));
            Assert.That(events["2001"].Images.Fanart, Is.Null);
            Assert.That(events["2001"].Fields["strVenue"]!.GetValue<string>(), Is.EqualTo("T-Mobile Arena"));
            Assert.That(events["2001"].Fields["intRound"]!.GetValue<string>(), Is.EqualTo("330"));
            Assert.That(events["2002"].Start, Is.EqualTo(new DateTime(2026, 4, 18, 19, 30, 0, DateTimeKind.Utc)));
            Assert.That(events["2002"].Status, Is.EqualTo(EventStatus.Scheduled));
            Assert.That(events["2003"].Start, Is.EqualTo(new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(events["1999"].Start, Is.EqualTo(new DateTime(2026, 3, 7, 23, 0, 0, DateTimeKind.Utc)));
            Assert.That(result.ItemErrors.Single().Errors, Is.EqualTo(new[] { "key: required" }));
            Assert.That(result.Meta["strLeague"]!.GetValue<string>(), Is.EqualTo("UFC"));
            Assert.That(result.Meta["strFanart"]!.GetValue<string>(), Does.EndWith("/ufc1.jpg"));
            Assert.That(result.Meta["strBadge"]!.GetValue<string>(), Does.EndWith("/ufc.png"));
            Assert.That(result.Responses.Select(r => r.Status), Is.All.EqualTo(200));
            Assert.That(result.Warnings, Is.Empty);
        });
    }

    [Test]
    public async Task Warns_when_a_season_returns_exactly_the_free_tier_cap()
    {
        var events = new JsonArray(Enumerable.Range(1, TheSportsDbSourceType.FreeSeasonCap)
            .Select(i => (JsonNode?)new JsonObject { ["idEvent"] = $"{i}", ["strEvent"] = $"Match {i}", ["strTimestamp"] = "2027-09-10T19:00:00" })
            .ToArray());
        _host.Handler.Add(Api + "eventsseason.php?id=4443&s=2027", new JsonObject { ["events"] = events }.ToJsonString());

        var result = await _host.FetchAsync("thesportsdb", new JsonObject
        {
            ["leagueId"] = "4443",
            ["seasons"] = "2027",
            ["includePast"] = false,
            ["includeFuture"] = false,
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Events, Has.Count.EqualTo(15));
            Assert.That(result.Warnings.Single(), Does.Contain("Season 2027 returned exactly 15 events"));
        });
    }

    [TestCase("all", new[] { "2022", "2023", "2024", "2025", "2026" })]
    [TestCase("2024..current", new[] { "2024", "2025", "2026" })]
    [TestCase("2023..2024", new[] { "2023", "2024" })]
    [TestCase("2025, current, 2025", new[] { "2025", "2026" })]
    public async Task Resolves_seasons(string seasons, string[] expected)
    {
        await _host.FetchAsync("thesportsdb", new JsonObject
        {
            ["leagueId"] = "4443",
            ["seasons"] = seasons.Contains(',') ? new JsonArray(seasons.Split(',').Select(s => (JsonNode?)s.Trim()).ToArray()) : seasons,
            ["includePast"] = false,
            ["includeFuture"] = false,
        });

        var fetched = _host.Handler.RequestedUrls.Where(u => u.Contains("eventsseason.php")).Select(u => u.Split("s=")[1]);
        Assert.That(fetched, Is.EqualTo(expected));
    }

    [Test]
    public void Validates_settings()
    {
        var failures = _host.Types.Get("thesportsdb").Validate(new JsonObject
        {
            ["apiKey"] = "../x",
            ["seasons"] = "2025..2024",
            ["requestDelay"] = -1,
        });

        Assert.That(failures.Select(f => f.PropertyName), Is.EquivalentTo(new[] { "apiKey", "leagueId", "seasons", "requestDelay" }));
    }

    [Test]
    public void Defaults_to_a_two_second_request_delay_and_no_timezone_setting()
    {
        var fields = _host.Types.Get("thesportsdb").Fields;

        Assert.Multiple(() =>
        {
            Assert.That(fields.Single(f => f.Name == "requestDelay").Value!.GetValue<double>(), Is.EqualTo(2));
            Assert.That(fields.Select(f => f.Name), Does.Not.Contain("timezone"));
            Assert.That(fields.Single(f => f.Name == "apiKey").Type, Is.EqualTo(SourceFieldType.Password));
        });
    }

    [Test]
    public async Task Waits_the_request_delay_between_requests()
    {
        await _host.FetchAsync("thesportsdb", new JsonObject { ["leagueId"] = "4443" });

        Assert.That(_host.Time.Delays, Is.EqualTo(Enumerable.Repeat(TimeSpan.FromSeconds(2), 3)));
    }

    [Test]
    public void Unknown_league_fails_the_fetch()
    {
        _host.Handler.Add(Api + "lookupleague.php?id=1", """{"leagues": null}""");

        var e = Assert.ThrowsAsync<SourceFetchException>(() => _host.FetchAsync("thesportsdb", new JsonObject { ["leagueId"] = "1" }));

        Assert.Multiple(() =>
        {
            Assert.That(e!.Message, Is.EqualTo("League 1 not found"));
            Assert.That(e.Responses, Has.Count.EqualTo(1));
        });
    }
}
