using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Generral.Integration.Test;

public class MappingApiTests
{
    private const string Events = """
        - key: final
          title: Final
          start: 2027-11-13T15:00:00Z
        - key: semi
          title: Semi-final
          start: 2027-11-10T15:00:00Z
        - key: friendly
          title: Friendly
          start: 2027-11-01T15:00:00Z
        """;

    private const string Definition = """
        # Knockout rounds only
        filter:
          - '{{ event.title != "Friendly" }}'
        title: 'Cup {{ event.title }}'
        search: { queries: ['Cup {{ event.title }}'] }
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
    public async Task A_saved_mapping_is_applied_and_kept_verbatim_with_its_versions()
    {
        var (seriesId, sourceId) = await SeedAsync();

        var created = await PostAsync("/api/v1/mapping", Mapping(seriesId, sourceId, Definition), HttpStatusCode.Created);
        var id = (int)created["id"]!;
        var applied = await WaitForCommandAsync(id, 1);
        var episodes = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/episode?seriesId={seriesId}"))!;

        var update = Mapping(seriesId, sourceId, Definition + "\noverview: Knockout");
        update["priority"] = 2;
        var updated = await PutAsync($"/api/v1/mapping/{id}", update);
        await WaitForCommandAsync(id, 2);
        update["name"] = "Cup renamed";
        update["definition"] = (string)updated["definition"]!;
        var renamed = await PutAsync($"/api/v1/mapping/{id}", update);
        await WaitForCommandAsync(id, 3);

        var versions = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/mapping/{id}/versions"))!;
        var all = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/mapping?seriesId={seriesId}"))!;
        var none = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/mapping?seriesId={seriesId + 1}"))!;
        var fetched = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/mapping/{id}"))!;

        Assert.Multiple(() =>
        {
            Assert.That((string)created["definition"]!, Is.EqualTo(Definition), "comments and layout are kept");
            Assert.That((int)created["version"]!, Is.EqualTo(1));
            Assert.That((bool)created["enabled"]!, Is.True);
            Assert.That((string)applied["status"]!, Is.EqualTo("completed"), (string?)applied["message"]);
            Assert.That((string)applied["message"]!, Is.EqualTo("Cup: 2 added, 0 changed, 0 unchanged, 0 orphaned, 0 errors"));
            Assert.That(episodes.Select(e => (string)e!["title"]!), Is.EqualTo(new[] { "Cup Semi-final", "Cup Final" }));
            Assert.That((int)updated["version"]!, Is.EqualTo(2));
            Assert.That((int)updated["priority"]!, Is.EqualTo(2));
            Assert.That((int)renamed["version"]!, Is.EqualTo(2), "a rename alone does not bump the version");
            Assert.That((string)fetched["name"]!, Is.EqualTo("Cup renamed"));
            Assert.That(versions.Select(v => (int)v!["version"]!), Is.EqualTo(new[] { 2, 1 }));
            Assert.That((string)versions[1]!["definition"]!, Is.EqualTo(Definition));
            Assert.That(versions[0]!["created"], Is.Not.Null);
            Assert.That(all.Select(m => (int)m!["id"]!), Is.EqualTo(new[] { id }));
            Assert.That(none, Is.Empty);
        });
    }

    [Test]
    public async Task Apply_enqueues_a_command_and_delete_orphans_the_episodes()
    {
        var (seriesId, sourceId) = await SeedAsync();
        var mapping = Mapping(seriesId, sourceId, Definition);
        mapping["enabled"] = false;
        var id = (int)(await PostAsync("/api/v1/mapping", mapping, HttpStatusCode.Created))["id"]!;
        var queuedOnSave = await ApplyCommandsAsync(id);

        var response = await _client.PostAsync($"/api/v1/mapping/{id}/apply", null);
        var command = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        await WaitForCommandAsync(id, 1);
        var missing = await _client.PostAsync($"/api/v1/mapping/{id + 1}/apply", null);

        var deleted = await _client.DeleteAsync($"/api/v1/mapping/{id}");
        var episodes = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/episode?seriesId={seriesId}&includeOrphaned=true"))!;
        var gone = await _client.GetAsync($"/api/v1/mapping/{id}");

        Assert.Multiple(() =>
        {
            Assert.That(queuedOnSave, Is.Empty, "disabled mappings are not applied on save");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That((string)command["name"]!, Is.EqualTo("ApplyMappings"));
            Assert.That((int)command["body"]!["mappingId"]!, Is.EqualTo(id));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(gone.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(episodes.Select(e => (string)e!["status"]!), Is.EqualTo(new[] { "orphaned", "orphaned" }));
            Assert.That(episodes.SelectMany(e => e!["links"]!.AsArray()), Is.Empty);
        });
    }

    [Test]
    public async Task Invalid_mappings_are_rejected_with_validation_failures()
    {
        var (seriesId, sourceId) = await SeedAsync();

        async Task<JsonArray> Failures(JsonObject resource, string url = "/api/v1/mapping", HttpMethod? method = null)
        {
            var response = await _client.SendAsync(new HttpRequestMessage(method ?? HttpMethod.Post, url) { Content = JsonContent.Create(resource) });
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<JsonArray>())!;
        }

        static string[] Names(JsonArray failures) => failures.Select(f => (string)f!["propertyName"]!).ToArray();

        var missing = await Failures(new JsonObject { ["seriesId"] = seriesId + 1, ["sourceId"] = sourceId + 1 });
        var yaml = await Failures(Mapping(seriesId, sourceId, "title: 'x'\nfilter: [\n"));
        var unknown = await Failures(Mapping(seriesId, sourceId, "titel: x"));
        var template = await Failures(Mapping(seriesId, sourceId, "title: '{{ if }}'"));

        var id = (int)(await PostAsync("/api/v1/mapping", Mapping(seriesId, sourceId, Definition), HttpStatusCode.Created))["id"]!;
        var moved = await Failures(Mapping(seriesId + 1, sourceId, Definition), $"/api/v1/mapping/{id}", HttpMethod.Put);
        var notFound = await _client.PutAsJsonAsync($"/api/v1/mapping/{id + 1}", Mapping(seriesId, sourceId, Definition));

        Assert.Multiple(() =>
        {
            Assert.That(Names(missing), Is.EqualTo(new[] { "name", "seriesId", "sourceId", "definition" }));
            Assert.That(Names(yaml), Is.EqualTo(new[] { "definition" }));
            Assert.That((string)yaml[0]!["errorMessage"]!, Does.StartWith("Invalid YAML at line"));
            Assert.That((string)unknown[0]!["errorMessage"]!, Does.StartWith("mapping: unknown property 'titel'"));
            Assert.That((string)template[0]!["errorMessage"]!, Does.StartWith("title: "));
            Assert.That(Names(moved), Is.EqualTo(new[] { "seriesId" }));
            Assert.That(notFound.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task Preview_evaluates_an_unsaved_definition_without_storing_anything()
    {
        var (seriesId, sourceId) = await SeedAsync();

        var fresh = await PostAsync("/api/v1/mapping/preview", new JsonObject { ["sourceId"] = sourceId, ["definition"] = Definition }, HttpStatusCode.OK);
        var episodes = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/episode?seriesId={seriesId}"))!;
        var mappings = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/mapping"))!;
        var rows = fresh["rows"]!.AsArray();
        var semi = rows.Single(r => (string)r!["event"]!["key"]! == "semi")!;
        var friendly = rows.Single(r => (string)r!["event"]!["key"]! == "friendly")!;

        Assert.Multiple(() =>
        {
            Assert.That(episodes, Is.Empty);
            Assert.That(mappings, Is.Empty);
            Assert.That(rows.Select(r => (string)r!["event"]!["key"]!), Is.EqualTo(new[] { "friendly", "semi", "final" }));
            Assert.That((bool)friendly["kept"]!, Is.False);
            Assert.That((string)friendly["rejectedBy"]!, Is.EqualTo("{{ event.title != \"Friendly\" }}"));
            Assert.That((string)friendly["change"]!, Is.EqualTo("filtered"));
            Assert.That((bool)semi["kept"]!, Is.True);
            Assert.That((int)semi["seasonNumber"]!, Is.EqualTo(2027));
            Assert.That((int)semi["episodeNumber"]!, Is.EqualTo(1));
            Assert.That((string)semi["title"]!, Is.EqualTo("Cup Semi-final"));
            Assert.That((string)semi["airDateUtc"]!, Is.EqualTo("2027-11-10T15:00:00Z"));
            Assert.That(semi["airDate"], Is.Not.Null);
            Assert.That((string)semi["searchTerms"]!["queries"]![0]!, Is.EqualTo("Cup Semi-final"));
            Assert.That((string)semi["change"]!, Is.EqualTo("new"));
            Assert.That(semi["errors"]!.AsArray(), Is.Empty);
            Assert.That(fresh["summary"]!.ToJsonString(), Is.EqualTo("""
                {"events":3,"kept":2,"episodes":2,"new":2,"changed":0,"unchanged":0,"orphaned":0,"filtered":1,"errors":0,"renumbered":0,"duplicates":0}
                """.Trim()));
            Assert.That((string)fresh["seasons"]![0]!["seasonNumber"]!.ToString(), Is.EqualTo("2027"));
            Assert.That(fresh["errors"]!.AsArray(), Is.Empty);
        });
    }

    [Test]
    public async Task Preview_of_a_saved_mapping_shows_changes_against_its_episodes()
    {
        var (seriesId, sourceId) = await SeedAsync();
        var id = (int)(await PostAsync("/api/v1/mapping", Mapping(seriesId, sourceId, Definition), HttpStatusCode.Created))["id"]!;
        await WaitForCommandAsync(id, 1);

        const string edited = """
            filter: ['{{ event.key != "final" }}']
            title: '{{ event.title }}'
            runtime: '{{ if event.key == "semi" }}abc{{ else }}90{{ end }}'
            """;
        var preview = await PostAsync(
            "/api/v1/mapping/preview", new JsonObject { ["id"] = id, ["sourceId"] = sourceId, ["definition"] = edited }, HttpStatusCode.OK);
        var unsavedOnly = await PostAsync(
            "/api/v1/mapping/preview", new JsonObject { ["seriesId"] = seriesId, ["sourceId"] = sourceId, ["definition"] = Definition }, HttpStatusCode.OK);
        var invalid = await _client.PostAsJsonAsync("/api/v1/mapping/preview", new JsonObject { ["sourceId"] = sourceId + 1, ["definition"] = "titel: x" });
        var invalidFailures = (await invalid.Content.ReadFromJsonAsync<JsonArray>())!;
        var versions = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/mapping/{id}/versions"))!;

        var changes = preview["rows"]!.AsArray().ToDictionary(r => (string)r!["event"]!["key"]!, r => (string)r!["change"]!);
        var friendly = preview["rows"]!.AsArray().Single(r => (string)r!["event"]!["key"]! == "friendly")!;
        var semi = preview["rows"]!.AsArray().Single(r => (string)r!["event"]!["key"]! == "semi")!;
        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(new Dictionary<string, string> { ["friendly"] = "new", ["semi"] = "error", ["final"] = "orphaned" }));
            Assert.That((int)friendly["episodeNumber"]!, Is.EqualTo(3), "orphaned and existing numbers are not reused");
            Assert.That((int)semi["currentEpisodeNumber"]!, Is.EqualTo(1));
            Assert.That(semi["errors"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That((string)preview["errors"]![0]!, Does.StartWith("semi: runtime"));
            Assert.That((int)preview["summary"]!["orphaned"]!, Is.EqualTo(1));
            Assert.That((int)preview["summary"]!["errors"]!, Is.EqualTo(1));
            Assert.That((int)unsavedOnly["summary"]!["new"]!, Is.EqualTo(2), "without the mapping id, its links are not known");
            Assert.That(unsavedOnly["rows"]!.AsArray().Where(r => (bool)r!["kept"]!).Select(r => (int)r!["episodeNumber"]!), Is.EqualTo(new[] { 3, 4 }));
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(invalidFailures.Select(f => (string)f!["propertyName"]!), Is.EqualTo(new[] { "sourceId", "definition" }));
            Assert.That(versions, Has.Count.EqualTo(1));
        });
    }

    private async Task<(int SeriesId, int SourceId)> SeedAsync()
    {
        var series = await PostAsync("/api/v1/series", new JsonObject { ["title"] = "Cup", ["path"] = "/tv/Cup" }, HttpStatusCode.Created);
        var source = await PostAsync(
            "/api/v1/source",
            new JsonObject
            {
                ["name"] = "Cup source",
                ["type"] = "static",
                ["fields"] = new JsonArray(
                    new JsonObject { ["name"] = "format", ["value"] = "yaml" },
                    new JsonObject { ["name"] = "events", ["value"] = Events }),
            },
            HttpStatusCode.Created);
        var sourceId = (int)source["id"]!;

        var refresh = await PostAsync($"/api/v1/source/{sourceId}/refresh", [], HttpStatusCode.Created);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while ((string)(await _client.GetFromJsonAsync<JsonObject>($"/api/v1/command/{(int)refresh["id"]!}"))!["status"]! is not ("completed" or "failed")
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        return ((int)series["id"]!, sourceId);
    }

    private static JsonObject Mapping(int seriesId, int sourceId, string definition) => new()
    {
        ["name"] = "Cup",
        ["seriesId"] = seriesId,
        ["sourceId"] = sourceId,
        ["definition"] = definition,
    };

    private async Task<JsonObject> PostAsync(string url, JsonObject body, HttpStatusCode expected)
    {
        var response = await _client.PostAsJsonAsync(url, body);
        Assert.That(response.StatusCode, Is.EqualTo(expected), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private async Task<JsonObject> PutAsync(string url, JsonObject body)
    {
        var response = await _client.PutAsJsonAsync(url, body);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private async Task<List<JsonObject>> ApplyCommandsAsync(int mappingId) =>
        (await _client.GetFromJsonAsync<JsonArray>("/api/v1/command"))!
            .Select(c => c!.AsObject())
            .Where(c => (string)c["name"]! == "ApplyMappings" && (int?)c["body"]!["mappingId"] == mappingId)
            .ToList();

    /// <summary>Waits until <paramref name="count"/> ApplyMappings commands of the mapping have finished; returns the newest.</summary>
    private async Task<JsonObject> WaitForCommandAsync(int mappingId, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var commands = await ApplyCommandsAsync(mappingId);
            if ((commands.Count >= count && commands.Take(count).All(c => (string)c["status"]! is "completed" or "failed")) || DateTime.UtcNow > deadline)
            {
                Assert.That(commands, Has.Count.EqualTo(count));
                return commands[0];
            }

            await Task.Delay(50);
        }
    }
}
