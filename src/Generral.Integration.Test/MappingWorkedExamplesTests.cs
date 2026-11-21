using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Generral.Integration.Test;

/// <summary>The worked examples of docs/specs/05-mapping.md (UFC numbered parts, Rugby World Cup) with synthetic events, across refreshes.</summary>
public class MappingWorkedExamplesTests
{
    private const string UfcMapping = """
        filter:
          - '{{ event.title | regex.match "^UFC \\d+\\b" }}'
        vars:
          number:   '{{ (event.title | regex.match "^UFC (\\d+)")[1] | string.to_int }}'
          headline: '{{ event.title | regex.replace "^UFC \\d+:?\\s*" "" }}'
        parts:
          - { key: early-prelims, title: Early Prelims, monitored: false, airOffset: -3h, runtime: 90 }
          - { key: prelims,       title: Prelims,       monitored: false, airOffset: -2h, runtime: 120 }
          - { key: main-card,     title: Main Card,     monitored: true,                  runtime: 180 }
        season:  { strategy: year }
        episode: { strategy: sequential, order: '{{ event.start }}', onInsert: renumber-unlocked }
        absolute: '{{ vars.number }}'
        title:    'UFC {{ vars.number }}{{ if vars.headline != "" }}: {{ vars.headline }}{{ end }} - {{ part.title }}'
        airDate:  '{{ event.start | date.add part.airOffset }}'
        runtime:  '{{ part.runtime }}'
        search:
          queries: ['UFC {{ vars.number }}']
          must: ['\bUFC ?{{ vars.number }}\b']
          parts:
            early-prelims: { must: ['\bearly ?prelims?\b'] }
            prelims:       { must: ['\bprelims?\b'], mustNot: ['\bearly ?prelims?\b'] }
            main-card:     { mustNot: ['\bprelims?\b'] }
        """;

    private const string RwcMapping = """
        dedupe: '{{ event.fields.home | string.slugify }}-{{ event.fields.away | string.slugify }}'
        season:  { strategy: fixed, value: 2027, title: '2027' }
        episode: { strategy: sequential, order: '{{ event.start }}' }
        title:   '{{ event.fields.stage ?? "Pool" }} - {{ event.fields.home }} vs {{ event.fields.away }}'
        """;

    private GenerralTestHost _host = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _host = await GenerralTestHost.StartAsync();
        _client = _host.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", _host.ApiKey);
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _host.DisposeAsync();
    }

    [Test]
    public async Task Ufc_numbered_parts_keep_their_numbers_across_refreshes()
    {
        var seriesId = await SeriesAsync("UFC");
        var sourceId = await SourceAsync("TheSportsDB – UFC", """
            - { key: '401', title: 'UFC 401: Jones vs. Aspinall', start: 2027-05-01T22:00:00Z }
            - { key: fn, title: 'UFC Fight Night: Edwards vs. Brady', start: 2027-05-15T22:00:00Z }
            - { key: '402', title: 'UFC 402', start: 2027-06-05T22:00:00Z }
            """);
        await PostAsync("/api/v1/mapping", Mapping("UFC", seriesId, sourceId, UfcMapping, 0), HttpStatusCode.Created);
        await WaitForCommandsAsync();
        var first = await EpisodesAsync(seriesId);

        await UpdateSourceAsync(sourceId, """
            - { key: '401', title: 'UFC 401: Jones vs. Aspinall', start: 2027-05-01T22:00:00Z }
            - { key: fn, title: 'UFC Fight Night: Edwards vs. Brady', start: 2027-05-15T22:00:00Z }
            - { key: '402', title: 'UFC 402: Pereira vs. Ankalaev', start: 2027-06-05T22:00:00Z }
            - { key: '403', title: 'UFC 403', start: 2027-07-10T22:00:00Z }
            """);
        var second = await EpisodesAsync(seriesId);
        var preview = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/series/{seriesId}/renumber-preview"))!;

        var prelims = second.Single(e => (string)e["title"]! == "UFC 401: Jones vs. Aspinall - Prelims");
        Assert.Multiple(() =>
        {
            Assert.That(Numbers(first), Is.EqualTo(new[]
            {
                "S2027E01 UFC 401: Jones vs. Aspinall - Early Prelims",
                "S2027E02 UFC 401: Jones vs. Aspinall - Prelims",
                "S2027E03 UFC 401: Jones vs. Aspinall - Main Card",
                "S2027E04 UFC 402 - Early Prelims",
                "S2027E05 UFC 402 - Prelims",
                "S2027E06 UFC 402 - Main Card",
            }));
            Assert.That(Numbers(second), Is.EqualTo(new[]
            {
                "S2027E01 UFC 401: Jones vs. Aspinall - Early Prelims",
                "S2027E02 UFC 401: Jones vs. Aspinall - Prelims",
                "S2027E03 UFC 401: Jones vs. Aspinall - Main Card",
                "S2027E04 UFC 402: Pereira vs. Ankalaev - Early Prelims",
                "S2027E05 UFC 402: Pereira vs. Ankalaev - Prelims",
                "S2027E06 UFC 402: Pereira vs. Ankalaev - Main Card",
                "S2027E07 UFC 403 - Early Prelims",
                "S2027E08 UFC 403 - Prelims",
                "S2027E09 UFC 403 - Main Card",
            }));
            Assert.That(Ids(second).Take(6), Is.EqualTo(Ids(first)), "the same episodes are updated");
            Assert.That(second.Select(e => (bool)e["monitored"]!), Is.EqualTo(new[] { false, false, true, false, false, true, false, false, true }));
            Assert.That((string)prelims["airDateUtc"]!, Is.EqualTo("2027-05-01T20:00:00Z"));
            Assert.That((int)prelims["runtime"]!, Is.EqualTo(120));
            Assert.That((string)prelims["links"]![0]!["partKey"]!, Is.EqualTo("prelims"));
            Assert.That(preview["episodes"]!.AsArray(), Is.Empty, "the numbering is already ideal");
        });
    }

    [Test]
    public async Task Rugby_world_cup_from_two_sources_is_deduplicated_stable_and_renumbered_on_request()
    {
        var seriesId = await SeriesAsync("Rugby World Cup");
        const string wiki = """
            - { key: m1, title: m1, start: 2027-10-01T19:00:00Z, home: France, away: New Zealand }
            - { key: m3, title: m3, start: 2027-10-03T19:00:00Z, home: Ireland, away: Scotland }
            """;
        var wikiId = await SourceAsync("Wikipedia – RWC", wiki);
        var patchId = await SourceAsync("RWC patches", """
            - { key: p1, title: p1, start: 2027-10-01T19:00:00Z, home: France, away: New Zealand }
            - { key: p2, title: p2, start: 2027-10-02T19:00:00Z, home: Wales, away: Fiji }
            """);
        await PostAsync("/api/v1/mapping", Mapping("RWC", seriesId, wikiId, RwcMapping, 0), HttpStatusCode.Created);
        await WaitForCommandsAsync();
        await PostAsync("/api/v1/mapping", Mapping("RWC patches", seriesId, patchId, RwcMapping, 1), HttpStatusCode.Created);
        await WaitForCommandsAsync();
        var first = await EpisodesAsync(seriesId);

        // Second refresh: the primary source now also has the patched match.
        await UpdateSourceAsync(wikiId, wiki + "\n- { key: m2, title: m2, start: 2027-10-02T19:00:00Z, home: Wales, away: Fiji }");
        await RefreshAsync(patchId);
        var second = await EpisodesAsync(seriesId);
        var links = second.ToDictionary(e => (string)e["title"]!, e => e["links"]!.AsArray().Select(l => (int)l!["mappingId"]!).ToList());

        var preview = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/series/{seriesId}/renumber-preview"))!;
        var stillSecond = await EpisodesAsync(seriesId);
        await PostAsync("/api/v1/command", new JsonObject { ["name"] = "Renumber", ["seriesId"] = seriesId }, HttpStatusCode.Created);
        await WaitForCommandsAsync();
        var renumbered = await EpisodesAsync(seriesId);
        var missing = await _client.GetAsync($"/api/v1/series/{seriesId + 1}/renumber-preview");

        Assert.Multiple(() =>
        {
            Assert.That(Numbers(first), Is.EqualTo(new[]
            {
                "S2027E01 Pool - France vs New Zealand",
                "S2027E02 Pool - Ireland vs Scotland",
                "S2027E03 Pool - Wales vs Fiji",
            }), "the patch's copy of France vs New Zealand is skipped, its extra match is appended");
            Assert.That(Numbers(second), Is.EqualTo(Numbers(first)));
            Assert.That(Ids(second), Is.EqualTo(Ids(first)));
            Assert.That(links["Pool - Wales vs Fiji"], Has.Count.EqualTo(1), "taken over by the higher-priority mapping");
            Assert.That(links["Pool - Wales vs Fiji"], Is.EqualTo(links["Pool - France vs New Zealand"]));
            Assert.That(preview["episodes"]!.AsArray().Select(e => $"{(string)e!["eventKey"]!} E{e["episodeNumber"]}→E{e["newEpisodeNumber"]}"),
                Is.EqualTo(new[] { "m2 E3→E2", "m3 E2→E3" }));
            Assert.That(Numbers(stillSecond), Is.EqualTo(Numbers(second)), "the preview stores nothing");
            Assert.That(Numbers(renumbered), Is.EqualTo(new[]
            {
                "S2027E01 Pool - France vs New Zealand",
                "S2027E02 Pool - Wales vs Fiji",
                "S2027E03 Pool - Ireland vs Scotland",
            }));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    private static List<string> Numbers(List<JsonObject> episodes) =>
        episodes.Select(e => $"S{(int)e["seasonNumber"]!:0000}E{(int)e["episodeNumber"]!:00} {(string)e["title"]!}").ToList();

    private static List<int> Ids(List<JsonObject> episodes) => episodes.Select(e => (int)e["id"]!).ToList();

    private async Task<List<JsonObject>> EpisodesAsync(int seriesId) =>
        (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/episode?seriesId={seriesId}"))!
            .Select(e => e!.AsObject())
            .OrderBy(e => (int)e["seasonNumber"]!).ThenBy(e => (int)e["episodeNumber"]!)
            .ToList();

    private async Task<int> SeriesAsync(string title) =>
        (int)(await PostAsync("/api/v1/series", new JsonObject { ["title"] = title, ["path"] = $"/tv/{title}" }, HttpStatusCode.Created))["id"]!;

    private async Task<int> SourceAsync(string name, string events)
    {
        var source = await PostAsync("/api/v1/source", new JsonObject { ["name"] = name, ["type"] = "static", ["fields"] = Fields(events) }, HttpStatusCode.Created);
        await RefreshAsync((int)source["id"]!);
        return (int)source["id"]!;
    }

    private async Task UpdateSourceAsync(int sourceId, string events)
    {
        var source = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/source/{sourceId}"))!;
        source["fields"] = Fields(events);
        (await _client.PutAsJsonAsync($"/api/v1/source/{sourceId}", source)).EnsureSuccessStatusCode();
        await RefreshAsync(sourceId);
    }

    private static JsonArray Fields(string events) => new(
        new JsonObject { ["name"] = "format", ["value"] = "yaml" },
        new JsonObject { ["name"] = "events", ["value"] = events });

    /// <summary>Refreshes the source and waits for it and the ApplyMappings commands it queues.</summary>
    private async Task RefreshAsync(int sourceId)
    {
        await PostAsync($"/api/v1/source/{sourceId}/refresh", [], HttpStatusCode.Created);
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

    private static JsonObject Mapping(string name, int seriesId, int sourceId, string definition, int priority) => new()
    {
        ["name"] = name,
        ["seriesId"] = seriesId,
        ["sourceId"] = sourceId,
        ["priority"] = priority,
        ["definition"] = definition,
    };

    private async Task<JsonObject> PostAsync(string url, JsonObject body, HttpStatusCode expected)
    {
        var response = await _client.PostAsJsonAsync(url, body);
        Assert.That(response.StatusCode, Is.EqualTo(expected), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }
}
