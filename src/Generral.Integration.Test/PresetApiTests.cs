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
/// M1 exit check: fictional numbered-events and tournament presets (Generral ships none), instantiated against synthetic
/// source responses and refreshed with changed data, keep stable and correct numbers.
/// </summary>
public class PresetApiTests
{
    private const string TsdbBase = "https://www.thesportsdb.com/api/v1/json/";
    private const string CupUrl = "https://example.org/cup/2027.json";

    private const string FightLeague = """
        schema: generral/preset@1
        id: example-fight-league
        name: Example Fight League – Numbered events
        description: Every numbered event of the fictional Example Fight League.
        tags: [example]
        parameters:
          - { name: tsdbKey, label: API key, default: "123", secret: true }
          - { name: seasons, label: Seasons, default: current }
        source:
          name: Example Fight League
          type: thesportsdb
          settings: { apiKey: "{{ secret.tsdbKey }}", leagueId: "9999", seasons: "{{ seasons }}", includePast: true, includeFuture: true }
        series: { title: EFL, genres: [Sport], seasonFolder: true }
        mapping:
          name: EFL numbered events
          filter: ['{{ event.title | regex.match "^EFL \\d+\\b" }}']
          vars:
            number:   '{{ (event.title | regex.match "^EFL (\\d+)")[1] | string.to_int }}'
            headline: '{{ event.title | regex.replace "^EFL \\d+:?\\s*" "" }}'
          season:  { strategy: year }
          episode: { strategy: sequential, order: '{{ event.start }}', onInsert: renumber-unlocked }
          absolute: '{{ vars.number }}'
          title:    'EFL {{ vars.number }}{{ if vars.headline != "" }}: {{ vars.headline }}{{ end }}SUFFIX'
          overview: '{{ [event.fields.strVenue, event.fields.strCity] | array.compact | array.join ", " }}'
          airDate:  '{{ event.start }}'
          PARTS
        """;

    private const string Cup = """
        schema: generral/preset@1
        id: example-cup
        name: Example Cup
        parameters:
          - { name: edition, label: Edition year, default: "2027" }
        source:
          name: "Example Cup {{ edition }}"
          type: http-json
          settings:
            requests: [{ url: "https://example.org/cup/{{ edition }}.json" }]
            items: "$[*]"
            fields:
              key:   { path: "$.MatchNumber" }
              home:  { path: "$.HomeTeam" }
              away:  { path: "$.AwayTeam" }
              start: { path: "$.DateUtc" }
              pool:  { path: "$.Group", optional: true }
              title: { text: "{{ home }} vs {{ away }}" }
        series: { title: Example Cup, genres: [Sport] }
        mapping:
          name: "Example Cup {{ edition }}"
          vars: { stage: '{{ event.fields.pool ?? "Knockout" }}' }
          season:  { strategy: fixed, value: '{{ edition }}' }
          episode: { strategy: sequential, order: '{{ event.start }}', onInsert: renumber-unlocked }
          title: '{{ vars.stage }} - {{ event.fields.home }} vs {{ event.fields.away }}'
          airDate: '{{ event.start }}'
        """;

    private const string Parts = """
        parts:
            - { key: undercard, title: Undercard, monitored: false }
            - { key: main-card, title: Main Card, monitored: true }
        """;

    private string _presetsFolder = null!;
    private FakeHandler _handler = null!;
    private GenerralTestHost _host = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _presetsFolder = Path.Combine(Path.GetTempPath(), "generral-presets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_presetsFolder);
        File.WriteAllText(Path.Combine(_presetsFolder, "example-fight-league.yaml"), FightLeague.Replace("SUFFIX", "").Replace("PARTS", ""));
        File.WriteAllText(Path.Combine(_presetsFolder, "example-fight-league-parts.yaml"), FightLeague
            .Replace("id: example-fight-league", "id: example-fight-league-parts")
            .Replace("SUFFIX", " - {{ part.title }}")
            .Replace("PARTS", Parts));
        File.WriteAllText(Path.Combine(_presetsFolder, "example-cup.yaml"), Cup);

        _handler = new FakeHandler();
        _host = await GenerralTestHost.StartAsync(new() { [Core.Presets.PresetStore.PresetsDirKey] = _presetsFolder }, services: services =>
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
        Directory.Delete(_presetsFolder, recursive: true);
    }

    [Test]
    public async Task Gallery_is_empty_without_a_presets_folder()
    {
        await using var host = await GenerralTestHost.StartAsync(new() { [Core.Presets.PresetStore.PresetsDirKey] = Path.Combine(_presetsFolder, "missing") });
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", host.ApiKey);

        Assert.That((await client.GetFromJsonAsync<JsonArray>("/api/v1/preset"))!, Is.Empty);
    }

    [Test]
    public async Task Gallery_lists_the_presets_folder()
    {
        var presets = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/preset"))!.Select(p => p!.AsObject()).ToDictionary(p => (string)p["id"]!);

        Assert.Multiple(() =>
        {
            Assert.That(presets.Keys, Is.EquivalentTo(new[] { "example-fight-league", "example-fight-league-parts", "example-cup" }));
            Assert.That((string)presets["example-fight-league"]["sourceType"]!, Is.EqualTo("thesportsdb"));
            Assert.That((string)presets["example-cup"]["sourceType"]!, Is.EqualTo("http-json"));
            Assert.That(presets["example-fight-league"]["requiredSecrets"]!.AsArray(), Is.Empty, "the free key is the default");
            Assert.That(presets["example-fight-league"]["parameters"]!.AsArray().Select(p => (string)p!["name"]!), Is.EqualTo(new[] { "tsdbKey", "seasons" }));
        });
    }

    [Test]
    public async Task Numbered_events_preset_numbers_stay_stable_across_refreshes()
    {
        TsdbSeason("abc123", """
            { "idEvent": "1", "strEvent": "EFL 30 Ward vs Kane", "strTimestamp": "2027-01-16T03:00:00", "strStatus": "NS", "strVenue": "Example Arena", "strCity": "Exampleton" },
            { "idEvent": "2", "strEvent": "EFL Showcase 12 Hale vs Ford", "strTimestamp": "2027-02-06T22:00:00" },
            { "idEvent": "3", "strEvent": "EFL 31 Mora vs Quinn", "strTimestamp": "2027-03-06T03:00:00" }
            """);

        var series = await PostAsync("/api/v1/preset/example-fight-league/instantiate", new JsonObject
        {
            ["rootFolderPath"] = "/tv",
            ["secrets"] = new JsonObject { ["tsdbKey"] = "abc123" },
        }, HttpStatusCode.Created);
        var seriesId = (int)series["id"]!;
        await WaitForCommandsAsync();
        var first = await EpisodesAsync(seriesId);

        // EFL 30 moves a week later, EFL 32 is announced.
        TsdbSeason("abc123", """
            { "idEvent": "1", "strEvent": "EFL 30 Ward vs Kane", "strTimestamp": "2027-01-23T03:00:00", "strStatus": "NS", "strVenue": "Example Arena", "strCity": "Exampleton" },
            { "idEvent": "2", "strEvent": "EFL Showcase 12 Hale vs Ford", "strTimestamp": "2027-02-06T22:00:00" },
            { "idEvent": "3", "strEvent": "EFL 31 Mora vs Quinn", "strTimestamp": "2027-03-06T03:00:00" },
            { "idEvent": "4", "strEvent": "EFL 32", "strTimestamp": "2027-04-10T03:00:00" }
            """);
        await RefreshAsync(seriesId);
        var second = await EpisodesAsync(seriesId);
        await RefreshAsync(seriesId);
        var third = await EpisodesAsync(seriesId);

        var sources = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/source"))!;
        Assert.Multiple(() =>
        {
            Assert.That((string)series["title"]!, Is.EqualTo("EFL"));
            Assert.That((string)series["path"]!, Is.EqualTo("/tv/EFL"));
            Assert.That(Numbers(first), Is.EqualTo(new[]
            {
                "S2027E01 #30 EFL 30: Ward vs Kane",
                "S2027E02 #31 EFL 31: Mora vs Quinn",
            }));
            Assert.That(Numbers(second), Is.EqualTo(new[]
            {
                "S2027E01 #30 EFL 30: Ward vs Kane",
                "S2027E02 #31 EFL 31: Mora vs Quinn",
                "S2027E03 #32 EFL 32",
            }));
            Assert.That(Numbers(third), Is.EqualTo(Numbers(second)));
            Assert.That(Ids(second).Take(2), Is.EqualTo(Ids(first)), "the same episodes are updated");
            Assert.That((string)second[0]["airDateUtc"]!, Is.EqualTo("2027-01-23T03:00:00Z"));
            Assert.That((string)second[0]["overview"]!, Is.EqualTo("Example Arena, Exampleton"));
            Assert.That(sources.Select(s => (string)s!["name"]!), Is.EqualTo(new[] { "Example Fight League" }));
            Assert.That(_handler.Urls, Has.All.StartWith(TsdbBase + "abc123/"));
        });
    }

    [Test]
    public async Task Tournament_preset_numbers_stay_stable_when_knockout_teams_are_known()
    {
        _handler.Set(CupUrl, Matches(
            Match(1, "2027-10-01 10:45:00Z", "Northland", "Eastmark", "Pool A"),
            Match(2, "2027-10-02 02:45:00Z", "Westvale", "Southport", "Pool F"),
            Match(49, "2027-11-06 09:00:00Z", "To be announced", "To be announced", null)));

        var series = await PostAsync("/api/v1/preset/example-cup/instantiate", new JsonObject
        {
            ["path"] = "/sport/Example Cup",
            ["monitored"] = false,
        }, HttpStatusCode.Created);
        var seriesId = (int)series["id"]!;
        await WaitForCommandsAsync();
        var first = await EpisodesAsync(seriesId);

        // The semi-final's teams are known, match 2 is moved before match 1, and match 3 is added.
        _handler.Set(CupUrl, Matches(
            Match(1, "2027-10-01 10:45:00Z", "Northland", "Eastmark", "Pool A"),
            Match(2, "2027-10-01 08:00:00Z", "Westvale", "Southport", "Pool F"),
            Match(3, "2027-10-03 08:00:00Z", "Isleton", "Highmoor", "Pool C"),
            Match(49, "2027-11-06 09:00:00Z", "Redcliff", "Bluewater", null)));
        await RefreshAsync(seriesId);
        var second = await EpisodesAsync(seriesId);

        // A second instance of the same edition reuses the identical source.
        var again = await _client.PostAsJsonAsync("/api/v1/preset/example-cup/instantiate", new JsonObject { ["title"] = "Example Cup 2027", ["path"] = "/sport/EC" });
        var sources = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/source"))!;

        Assert.Multiple(() =>
        {
            Assert.That((bool)series["monitored"]!, Is.False);
            Assert.That(Numbers(first), Is.EqualTo(new[]
            {
                "S2027E01 # Pool A - Northland vs Eastmark",
                "S2027E02 # Pool F - Westvale vs Southport",
                "S2027E03 # Knockout - To be announced vs To be announced",
            }));
            // renumber-unlocked: nothing has a file yet, so the order follows the schedule.
            Assert.That(Numbers(second), Is.EqualTo(new[]
            {
                "S2027E01 # Pool F - Westvale vs Southport",
                "S2027E02 # Pool A - Northland vs Eastmark",
                "S2027E03 # Pool C - Isleton vs Highmoor",
                "S2027E04 # Knockout - Redcliff vs Bluewater",
            }));
            Assert.That(second.Single(e => ((string)e["title"]!).Contains("Bluewater"))["id"]!.GetValue<int>(), Is.EqualTo(Ids(first)[2]), "the knockout episode is the same one");
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(sources, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Export_then_import_round_trips_without_secrets()
    {
        TsdbSeason("secretkey", """{ "idEvent": "1", "strEvent": "EFL 30", "strTimestamp": "2027-01-16T03:00:00" }""");
        var series = await PostAsync("/api/v1/preset/example-fight-league-parts/instantiate", new JsonObject
        {
            ["rootFolderPath"] = "/tv",
            ["secrets"] = new JsonObject { ["tsdbKey"] = "secretkey" },
        }, HttpStatusCode.Created);
        await WaitForCommandsAsync();

        var exported = await (await _client.PostAsync($"/api/v1/series/{series["id"]}/export", null)).Content.ReadAsStringAsync();
        var invalid = await _client.PostAsync("/api/v1/preset/import", new StringContent("schema: generral/preset@1\nid: x\n", Encoding.UTF8, "application/yaml"));
        var imported = await _client.PostAsync("/api/v1/preset/import", new StringContent(exported.Replace("id: efl", "id: my-efl"), Encoding.UTF8, "application/yaml"));
        var body = (await imported.Content.ReadFromJsonAsync<JsonObject>())!;
        var instance = await PostAsync("/api/v1/preset/my-efl/instantiate", new JsonObject
        {
            ["title"] = "EFL again",
            ["rootFolderPath"] = "/tv",
            ["secrets"] = new JsonObject { ["apiKey"] = "secretkey" },
        }, HttpStatusCode.Created);
        await WaitForCommandsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exported, Does.Not.Contain("secretkey"));
            Assert.That(exported, Does.Contain("{{ secret.apiKey }}"));
            Assert.That(exported, Does.Contain("Undercard"));
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(imported.StatusCode, Is.EqualTo(HttpStatusCode.Created), body.ToJsonString());
            Assert.That(body["requiredSecrets"]!.AsArray().Select(s => (string)s!), Is.EqualTo(new[] { "apiKey" }));
            Assert.That(File.Exists(Path.Combine(_host.ConfigFolder, "presets", "my-efl.yaml")));
            Assert.That((string)instance["title"]!, Is.EqualTo("EFL again"));
        });
        Assert.That(Numbers(await EpisodesAsync((int)instance["id"]!)), Is.EqualTo(Numbers(await EpisodesAsync((int)series["id"]!))));
    }

    [Test]
    public async Task Instantiate_validates_the_options()
    {
        var missing = await _client.PostAsJsonAsync("/api/v1/preset/nope/instantiate", new JsonObject());
        var noPath = await _client.PostAsJsonAsync("/api/v1/preset/example-fight-league/instantiate", new JsonObject());
        var badKey = await _client.PostAsJsonAsync("/api/v1/preset/example-fight-league/instantiate", new JsonObject
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
        var league = """{ "leagues": [{ "idLeague": "9999", "strLeague": "Example Fight League", "strCurrentSeason": "2027" }] }""";
        _handler.Set($"{TsdbBase}{key}/lookupleague.php?id=9999", league);
        _handler.Set($"{TsdbBase}{key}/eventsseason.php?id=9999&s=2027", $$"""{ "events": [{{events}}] }""");
        _handler.Set($"{TsdbBase}{key}/eventspastleague.php?id=9999", """{ "events": null }""");
        _handler.Set($"{TsdbBase}{key}/eventsnextleague.php?id=9999", """{ "events": null }""");
    }

    private static string Match(int number, string date, string home, string away, string? pool) =>
        new JsonObject
        {
            ["MatchNumber"] = number,
            ["RoundNumber"] = 1,
            ["DateUtc"] = date,
            ["Location"] = "Example Park",
            ["HomeTeam"] = home,
            ["AwayTeam"] = away,
            ["Group"] = pool,
            ["HomeTeamScore"] = null,
        }.ToJsonString();

    private static string Matches(params string[] matches) => "[" + string.Join(",", matches) + "]";

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
