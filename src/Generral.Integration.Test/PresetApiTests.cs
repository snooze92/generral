using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Generral.Core.Sources.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Generral.Integration.Test;

/// <summary>
/// M1 exit check: the shipped UFC and Rugby World Cup presets, instantiated against synthetic source responses and refreshed
/// with changed data, keep stable and correct numbers.
/// </summary>
public class PresetApiTests
{
    private const string TsdbBase = "https://www.thesportsdb.com/api/v1/json/";
    private const string RwcUrl = "https://fixturedownload.com/feed/json/rugby-world-cup-2027";

    private FakeHandler _handler = null!;
    private GenerralTestHost _host = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _handler = new FakeHandler();
        _host = await GenerralTestHost.StartAsync(services: services =>
        {
            services.AddSingleton<TimeProvider>(new NoDelayTime());
            services.PostConfigure<HttpClientFactoryOptions>(SourceFetcherFactory.ClientName,
                o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = _handler));
        });
        _client = _host.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", _host.ApiKey);
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        _handler.Dispose();
    }

    [Test]
    public async Task Gallery_lists_the_shipped_presets()
    {
        var presets = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/preset"))!.Select(p => p!.AsObject()).ToDictionary(p => (string)p["id"]!);

        Assert.Multiple(() =>
        {
            Assert.That(presets.Keys, Is.EquivalentTo(new[] { "ufc-numbered", "ufc-numbered-parts", "rugby-world-cup" }));
            Assert.That((string)presets["ufc-numbered"]["sourceType"]!, Is.EqualTo("thesportsdb"));
            Assert.That((string)presets["rugby-world-cup"]["sourceType"]!, Is.EqualTo("http-json"));
            Assert.That(presets["ufc-numbered"]["requiredSecrets"]!.AsArray(), Is.Empty, "the free key is the default");
            Assert.That(presets["ufc-numbered"]["parameters"]!.AsArray().Select(p => (string)p!["name"]!), Is.EqualTo(new[] { "tsdbKey", "seasons" }));
        });
    }

    [Test]
    public async Task Ufc_preset_numbers_stay_stable_across_refreshes()
    {
        TsdbSeason("abc123", """
            { "idEvent": "1", "strEvent": "UFC 330 Jones vs Aspinall", "strTimestamp": "2027-01-16T03:00:00", "strStatus": "NS", "strVenue": "T-Mobile Arena", "strCity": "Las Vegas" },
            { "idEvent": "2", "strEvent": "UFC Fight Night 300 Edwards vs Brady", "strTimestamp": "2027-02-06T22:00:00" },
            { "idEvent": "3", "strEvent": "UFC 331 Pereira vs Ankalaev", "strTimestamp": "2027-03-06T03:00:00" }
            """);

        var series = await PostAsync("/api/v1/preset/ufc-numbered/instantiate", new JsonObject
        {
            ["rootFolderPath"] = "/tv",
            ["secrets"] = new JsonObject { ["tsdbKey"] = "abc123" },
        }, HttpStatusCode.Created);
        var seriesId = (int)series["id"]!;
        await WaitForCommandsAsync();
        var first = await EpisodesAsync(seriesId);

        // UFC 330 moves a week later, UFC 332 is announced.
        TsdbSeason("abc123", """
            { "idEvent": "1", "strEvent": "UFC 330 Jones vs Aspinall", "strTimestamp": "2027-01-23T03:00:00", "strStatus": "NS", "strVenue": "T-Mobile Arena", "strCity": "Las Vegas" },
            { "idEvent": "2", "strEvent": "UFC Fight Night 300 Edwards vs Brady", "strTimestamp": "2027-02-06T22:00:00" },
            { "idEvent": "3", "strEvent": "UFC 331 Pereira vs Ankalaev", "strTimestamp": "2027-03-06T03:00:00" },
            { "idEvent": "4", "strEvent": "UFC 332", "strTimestamp": "2027-04-10T03:00:00" }
            """);
        await RefreshAsync(seriesId);
        var second = await EpisodesAsync(seriesId);
        await RefreshAsync(seriesId);
        var third = await EpisodesAsync(seriesId);

        var sources = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/source"))!;
        Assert.Multiple(() =>
        {
            Assert.That((string)series["title"]!, Is.EqualTo("UFC"));
            Assert.That((string)series["path"]!, Is.EqualTo("/tv/UFC"));
            Assert.That(Numbers(first), Is.EqualTo(new[]
            {
                "S2027E01 #330 UFC 330: Jones vs Aspinall",
                "S2027E02 #331 UFC 331: Pereira vs Ankalaev",
            }));
            Assert.That(Numbers(second), Is.EqualTo(new[]
            {
                "S2027E01 #330 UFC 330: Jones vs Aspinall",
                "S2027E02 #331 UFC 331: Pereira vs Ankalaev",
                "S2027E03 #332 UFC 332",
            }));
            Assert.That(Numbers(third), Is.EqualTo(Numbers(second)));
            Assert.That(Ids(second).Take(2), Is.EqualTo(Ids(first)), "the same episodes are updated");
            Assert.That((string)second[0]["airDateUtc"]!, Is.EqualTo("2027-01-23T03:00:00Z"));
            Assert.That((string)second[0]["overview"]!, Is.EqualTo("T-Mobile Arena, Las Vegas"));
            Assert.That(sources.Select(s => (string)s!["name"]!), Is.EqualTo(new[] { "TheSportsDB – UFC" }));
            Assert.That(_handler.Urls, Has.All.StartWith(TsdbBase + "abc123/"));
        });
    }

    [Test]
    public async Task Rugby_world_cup_preset_numbers_stay_stable_when_knockout_teams_are_known()
    {
        _handler.Set(RwcUrl, Rwc(
            Match(1, "2027-10-01 10:45:00Z", "Australia", "Hong Kong China", "Pool A"),
            Match(2, "2027-10-02 02:45:00Z", "Wales", "Zimbabwe", "Pool F"),
            Match(49, "2027-11-06 09:00:00Z", "To be announced", "To be announced", null)));

        var series = await PostAsync("/api/v1/preset/rugby-world-cup/instantiate", new JsonObject
        {
            ["path"] = "/sport/Rugby World Cup",
            ["monitored"] = false,
        }, HttpStatusCode.Created);
        var seriesId = (int)series["id"]!;
        await WaitForCommandsAsync();
        var first = await EpisodesAsync(seriesId);

        // The semi-final's teams are known, match 2 is moved before match 1, and match 3 is added.
        _handler.Set(RwcUrl, Rwc(
            Match(1, "2027-10-01 10:45:00Z", "Australia", "Hong Kong China", "Pool A"),
            Match(2, "2027-10-01 08:00:00Z", "Wales", "Zimbabwe", "Pool F"),
            Match(3, "2027-10-03 08:00:00Z", "Ireland", "Scotland", "Pool C"),
            Match(49, "2027-11-06 09:00:00Z", "New Zealand", "South Africa", null)));
        await RefreshAsync(seriesId);
        var second = await EpisodesAsync(seriesId);

        // A second instance of the same edition reuses the identical source.
        var again = await _client.PostAsJsonAsync("/api/v1/preset/rugby-world-cup/instantiate", new JsonObject { ["title"] = "RWC 2027", ["path"] = "/sport/RWC" });
        var sources = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/source"))!;

        Assert.Multiple(() =>
        {
            Assert.That((bool)series["monitored"]!, Is.False);
            Assert.That(Numbers(first), Is.EqualTo(new[]
            {
                "S2027E01 # Pool A - Australia vs Hong Kong China",
                "S2027E02 # Pool F - Wales vs Zimbabwe",
                "S2027E03 # Knockout - To be announced vs To be announced",
            }));
            // renumber-unlocked: nothing has a file yet, so the order follows the schedule.
            Assert.That(Numbers(second), Is.EqualTo(new[]
            {
                "S2027E01 # Pool F - Wales vs Zimbabwe",
                "S2027E02 # Pool A - Australia vs Hong Kong China",
                "S2027E03 # Pool C - Ireland vs Scotland",
                "S2027E04 # Knockout - New Zealand vs South Africa",
            }));
            Assert.That(second.Single(e => ((string)e["title"]!).Contains("South Africa"))["id"]!.GetValue<int>(), Is.EqualTo(Ids(first)[2]), "the knockout episode is the same one");
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(sources, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Export_then_import_round_trips_without_secrets()
    {
        TsdbSeason("secretkey", """{ "idEvent": "1", "strEvent": "UFC 330", "strTimestamp": "2027-01-16T03:00:00" }""");
        var series = await PostAsync("/api/v1/preset/ufc-numbered-parts/instantiate", new JsonObject
        {
            ["rootFolderPath"] = "/tv",
            ["secrets"] = new JsonObject { ["tsdbKey"] = "secretkey" },
        }, HttpStatusCode.Created);
        await WaitForCommandsAsync();

        var exported = await (await _client.PostAsync($"/api/v1/series/{series["id"]}/export", null)).Content.ReadAsStringAsync();
        var invalid = await _client.PostAsync("/api/v1/preset/import", new StringContent("schema: generral/preset@1\nid: x\n", Encoding.UTF8, "application/yaml"));
        var imported = await _client.PostAsync("/api/v1/preset/import", new StringContent(exported.Replace("id: ufc", "id: my-ufc"), Encoding.UTF8, "application/yaml"));
        var body = (await imported.Content.ReadFromJsonAsync<JsonObject>())!;
        var instance = await PostAsync("/api/v1/preset/my-ufc/instantiate", new JsonObject
        {
            ["title"] = "UFC again",
            ["rootFolderPath"] = "/tv",
            ["secrets"] = new JsonObject { ["apiKey"] = "secretkey" },
        }, HttpStatusCode.Created);
        await WaitForCommandsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exported, Does.Not.Contain("secretkey"));
            Assert.That(exported, Does.Contain("{{ secret.apiKey }}"));
            Assert.That(exported, Does.Contain("Early Prelims"));
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(imported.StatusCode, Is.EqualTo(HttpStatusCode.Created), body.ToJsonString());
            Assert.That(body["requiredSecrets"]!.AsArray().Select(s => (string)s!), Is.EqualTo(new[] { "apiKey" }));
            Assert.That(File.Exists(Path.Combine(_host.ConfigFolder, "presets", "my-ufc.yaml")));
            Assert.That((string)instance["title"]!, Is.EqualTo("UFC again"));
        });
        Assert.That(Numbers(await EpisodesAsync((int)instance["id"]!)), Is.EqualTo(Numbers(await EpisodesAsync((int)series["id"]!))));
    }

    [Test]
    public async Task Instantiate_validates_the_options()
    {
        var missing = await _client.PostAsJsonAsync("/api/v1/preset/nope/instantiate", new JsonObject());
        var noPath = await _client.PostAsJsonAsync("/api/v1/preset/ufc-numbered/instantiate", new JsonObject());
        var badKey = await _client.PostAsJsonAsync("/api/v1/preset/ufc-numbered/instantiate", new JsonObject
        {
            ["rootFolderPath"] = "/tv",
            ["secrets"] = new JsonObject { ["tsdbKey"] = "not a key!" },
        });

        Assert.Multiple(async () =>
        {
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(noPath.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await badKey.Content.ReadAsStringAsync(), Does.Contain("source.settings.apiKey"));
            Assert.That((await _client.GetFromJsonAsync<JsonArray>("/api/v1/series"))!, Is.Empty);
        });
    }

    private void TsdbSeason(string key, string events)
    {
        var league = """{ "leagues": [{ "idLeague": "4443", "strLeague": "UFC", "strCurrentSeason": "2027" }] }""";
        _handler.Set($"{TsdbBase}{key}/lookupleague.php?id=4443", league);
        _handler.Set($"{TsdbBase}{key}/eventsseason.php?id=4443&s=2027", $$"""{ "events": [{{events}}] }""");
        _handler.Set($"{TsdbBase}{key}/eventspastleague.php?id=4443", """{ "events": null }""");
        _handler.Set($"{TsdbBase}{key}/eventsnextleague.php?id=4443", """{ "events": null }""");
    }

    private static string Match(int number, string date, string home, string away, string? pool) =>
        new JsonObject
        {
            ["MatchNumber"] = number,
            ["RoundNumber"] = 1,
            ["DateUtc"] = date,
            ["Location"] = "Perth Stadium",
            ["HomeTeam"] = home,
            ["AwayTeam"] = away,
            ["Group"] = pool,
            ["HomeTeamScore"] = null,
        }.ToJsonString();

    private static string Rwc(params string[] matches) => "[" + string.Join(",", matches) + "]";

    private static List<string> Numbers(List<JsonObject> episodes) =>
        episodes.Select(e => $"S{(int)e["seasonNumber"]!:0000}E{(int)e["episodeNumber"]!:00} #{e["absoluteEpisodeNumber"]} {(string)e["title"]!}").ToList();

    private static List<int> Ids(List<JsonObject> episodes) => episodes.Select(e => (int)e["id"]!).ToList();

    private async Task<List<JsonObject>> EpisodesAsync(int seriesId) =>
        (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/episode?seriesId={seriesId}"))!
            .Select(e => e!.AsObject())
            .OrderBy(e => (int)e["seasonNumber"]!).ThenBy(e => (int)e["episodeNumber"]!)
            .ToList();

    private async Task RefreshAsync(int seriesId)
    {
        var mapping = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/mapping"))!.First(m => (int)m!["seriesId"]! == seriesId)!;
        await PostAsync($"/api/v1/source/{mapping["sourceId"]}/refresh", [], HttpStatusCode.Created);
        await WaitForCommandsAsync();
    }

    private async Task WaitForCommandsAsync()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var commands = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/command"))!;
            if (commands.All(c => (string)c!["status"]! is "completed" or "failed"))
            {
                Assert.That(commands.Where(c => (string)c!["status"]! == "failed").Select(c => (string?)c!["message"]), Is.Empty);
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("commands did not finish");
    }

    private async Task<JsonObject> PostAsync(string url, JsonObject body, HttpStatusCode expected)
    {
        var response = await _client.PostAsJsonAsync(url, body);
        Assert.That(response.StatusCode, Is.EqualTo(expected), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, string> _responses = new();

        public ConcurrentQueue<string> Urls { get; } = new();

        public void Set(string url, string json) => _responses[new Uri(url).AbsoluteUri] = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Urls.Enqueue(url);
            return Task.FromResult(_responses.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>The system clock, but delays (the sources' request delay) end at once.</summary>
    private sealed class NoDelayTime : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            System.CreateTimer(callback, state, dueTime > TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : dueTime, period);
    }
}
