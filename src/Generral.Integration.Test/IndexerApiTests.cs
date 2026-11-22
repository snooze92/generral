using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Generral.Core.Indexers.Torznab;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Generral.Integration.Test;

public class IndexerApiTests
{
    private const string Caps = """
        <caps><searching><search available="yes" supportedParams="q"/><tv-search available="yes" supportedParams="q,season,ep"/></searching>
        <categories><category id="5000" name="TV"><subcat id="5060" name="TV/Sport"/></category></categories></caps>
        """;

    private CapsHandler _handler = null!;
    private GenerralTestHost _host = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _handler = new CapsHandler();
        _host = await GenerralTestHost.StartAsync(services: services =>
            services.PostConfigure<HttpClientFactoryOptions>(TorznabClient.ClientName,
                o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = _handler)));
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
    public async Task Status_reports_a_sonarr_compatible_version()
    {
        var response = await _client.GetAsync("/api/v3/system/status");
        var status = (await response.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Multiple(() =>
        {
            Assert.That((string)status["appName"]!, Is.EqualTo("Generral"));
            Assert.That(Version.Parse((string)status["version"]!), Is.GreaterThanOrEqualTo(new Version(4, 0, 0, 0)));
            Assert.That(status["generralVersion"], Is.Not.Null);
            Assert.That(Version.Parse(response.Headers.GetValues("X-Application-Version").Single()), Is.GreaterThanOrEqualTo(new Version(3, 0, 5, 0)));
        });
    }

    [Test]
    public async Task The_shim_requires_the_api_key()
    {
        using var anonymous = _host.CreateClient();

        var response = await anonymous.GetAsync("/api/v3/indexer");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Schema_has_torznab_and_newznab_with_sonarr_field_names()
    {
        var schema = (await _client.GetFromJsonAsync<JsonArray>("/api/v3/indexer/schema"))!;

        var torznab = schema.Single(s => (string)s!["implementation"]! == "Torznab")!;
        var fields = torznab["fields"]!.AsArray().Select(f => (string)f!["name"]!).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(schema.Select(s => (string)s!["implementation"]!), Is.EquivalentTo(new[] { "Newznab", "Torznab" }));
            Assert.That((string)torznab["configContract"]!, Is.EqualTo("TorznabSettings"));
            Assert.That(fields, Is.SupersetOf(new[]
            {
                "baseUrl", "apiPath", "apiKey", "categories", "animeCategories", "animeStandardFormatSearch", "additionalParameters",
                "multiLanguages", "failDownloads", "minimumSeeders", "seedCriteria.seedRatio", "seedCriteria.seedTime",
                "seedCriteria.seasonPackSeedTime", "rejectBlocklistedTorrentHashesWhileGrabbing",
            }));
            Assert.That(torznab["fields"]!.AsArray().Single(f => (string)f!["name"]! == "categories")!["value"]!.AsArray().Select(c => (int)c!), Is.EqualTo(new[] { 5000, 5060 }));
        });
    }

    [Test]
    public async Task Prowlarr_sync_round_trips()
    {
        var schema = (await _client.GetFromJsonAsync<JsonArray>("/api/v3/indexer/schema"))!;
        var payload = ProwlarrIndexer(schema, id: 0, categories: [5000, 5060]);

        // Prowlarr's app test: a POST to /indexer/test, then the X-Application-Version check.
        var test = await _client.PostAsJsonAsync("/api/v3/indexer/test", payload);
        Assert.That(test.StatusCode, Is.EqualTo(HttpStatusCode.OK), await test.Content.ReadAsStringAsync());
        Assert.That(Version.Parse(test.Headers.GetValues("X-Application-Version").Single()), Is.GreaterThanOrEqualTo(new Version(3, 0, 5, 0)));

        var created = await _client.PostAsJsonAsync("/api/v3/indexer", payload);
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        var id = (int)(await created.Content.ReadFromJsonAsync<JsonObject>())!["id"]!;

        var all = (await _client.GetFromJsonAsync<JsonArray>("/api/v3/indexer"))!;
        var remote = (await _client.GetFromJsonAsync<JsonObject>($"/api/v3/indexer/{id}"))!;
        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(1));
            AssertProwlarrEquals(ProwlarrIndexer(schema, id, categories: [5000, 5060]), remote);
            Assert.That(_handler.Urls, Has.Some.EqualTo("http://prowlarr:9696/1/api?t=caps&apikey=prowlarr-key"));
        });

        // A user field Prowlarr doesn't manage, then Prowlarr's update (it merges back the remote's other fields).
        var user = remote.DeepClone().AsObject();
        user["fields"]!.AsArray().Single(f => (string)f!["name"]! == "minimumSeeders")!["value"] = 3;
        user["tags"] = new JsonArray();
        Assert.That((await _client.PutAsJsonAsync($"/api/v1/indexer/{id}", user)).StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var update = ProwlarrIndexer(schema, id, categories: [5000]);
        update["fields"]!.AsArray().Single(f => (string)f!["name"]! == "minimumSeeders")!["value"] = 3;
        var remoteFields = remote["fields"]!.AsArray().Where(f => update["fields"]!.AsArray().All(u => (string)u!["name"]! != (string)f!["name"]!));
        foreach (var field in remoteFields)
        {
            update["fields"]!.AsArray().Add(field!.DeepClone());
        }

        _handler.Down = true;
        var rejected = await _client.PutAsJsonAsync($"/api/v3/indexer/{id}", update);
        var forced = await _client.PutAsJsonAsync($"/api/v3/indexer/{id}?forceSave=true", update);
        var updated = (await _client.GetFromJsonAsync<JsonObject>($"/api/v3/indexer/{id}"))!;
        Assert.Multiple(() =>
        {
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "caps unreachable");
            Assert.That(forced.StatusCode, Is.EqualTo(HttpStatusCode.Accepted), "forceSave skips the test");
            Assert.That(Field(updated, "categories")!.AsArray().Select(c => (int)c!), Is.EqualTo(new[] { 5000 }));
            Assert.That((int)Field(updated, "minimumSeeders")!, Is.EqualTo(3));
            Assert.That((string)Field(updated, "apiKey")!, Is.EqualTo("********"));
        });

        Assert.That((await _client.DeleteAsync($"/api/v3/indexer/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await _client.GetAsync($"/api/v3/indexer/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Native_crud_validates_and_stores_newznab_disabled_for_search()
    {
        var invalid = await _client.PostAsJsonAsync("/api/v1/indexer", new JsonObject
        {
            ["name"] = "Manual",
            ["implementation"] = "Torznab",
            ["priority"] = 60,
            ["fields"] = new JsonArray(new JsonObject { ["name"] = "baseUrl", ["value"] = "ftp://nope" }),
        });
        var newznab = await _client.PostAsJsonAsync("/api/v1/indexer?forceSave=true", new JsonObject
        {
            ["name"] = "Usenet (Prowlarr)",
            ["implementation"] = "Newznab",
            ["configContract"] = "NewznabSettings",
            ["enableRss"] = true,
            ["fields"] = new JsonArray(new JsonObject { ["name"] = "baseUrl", ["value"] = "http://prowlarr:9696/2/" }),
        });
        var stored = (await newznab.Content.ReadFromJsonAsync<JsonObject>())!;

        var errors = (await invalid.Content.ReadFromJsonAsync<JsonArray>())!.Select(e => (string)e!["propertyName"]!).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(errors, Is.SupersetOf(new[] { "priority", "baseUrl" }));
            Assert.That(newznab.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That((bool)stored["supportsSearch"]!, Is.False);
            Assert.That((string)stored["message"]!["type"]!, Is.EqualTo("warning"));
            Assert.That((string)stored["protocol"]!, Is.EqualTo("usenet"));
        });
    }

    /// <summary>What Prowlarr's <c>Sonarr.BuildSonarrIndexer</c> posts: the synced fields from the schema, with its values.</summary>
    private static JsonObject ProwlarrIndexer(JsonArray schema, int id, int[] categories)
    {
        var torznab = schema.Single(s => (string)s!["implementation"]! == "Torznab")!;
        string[] synced =
        [
            "baseUrl", "apiPath", "apiKey", "categories", "animeCategories", "animeStandardFormatSearch", "minimumSeeders",
            "seedCriteria.seedRatio", "seedCriteria.seedTime", "seedCriteria.seasonPackSeedTime", "rejectBlocklistedTorrentHashesWhileGrabbing",
            .. id == 0 ? ["additionalParameters"] : Array.Empty<string>(),
        ];
        var values = new Dictionary<string, JsonNode?>
        {
            ["baseUrl"] = "http://prowlarr:9696/1/",
            ["apiPath"] = "/api",
            ["apiKey"] = "prowlarr-key",
            ["categories"] = new JsonArray(categories.Select(c => (JsonNode)c).ToArray()),
            ["animeCategories"] = new JsonArray(),
            ["animeStandardFormatSearch"] = false,
            ["minimumSeeders"] = 1,
            ["seedCriteria.seedRatio"] = 1.5,
            ["seedCriteria.seedTime"] = null,
            ["seedCriteria.seasonPackSeedTime"] = null,
            ["rejectBlocklistedTorrentHashesWhileGrabbing"] = false,
        };

        var fields = torznab["fields"]!.AsArray()
            .Where(f => synced.Contains((string)f!["name"]!))
            .Select(f =>
            {
                var name = (string)f!["name"]!;
                return (JsonNode)new JsonObject
                {
                    ["name"] = name,
                    ["value"] = values.TryGetValue(name, out var v) ? v?.DeepClone() : f["value"]?.DeepClone(),
                    ["type"] = f["type"]?.DeepClone(),
                    ["advanced"] = f["advanced"]?.DeepClone(),
                    ["section"] = null,
                    ["hidden"] = null,
                };
            });

        return new JsonObject
        {
            ["id"] = id,
            ["enableRss"] = true,
            ["enableAutomaticSearch"] = true,
            ["enableInteractiveSearch"] = true,
            ["priority"] = 25,
            ["name"] = "Tracker (Prowlarr)",
            ["implementationName"] = null,
            ["implementation"] = "Torznab",
            ["configContract"] = (string)torznab["configContract"]!,
            ["infoLink"] = null,
            ["downloadClientId"] = null,
            ["seasonSearchMaximumSingleEpisodeAge"] = null,
            ["tags"] = new JsonArray(),
            ["fields"] = new JsonArray(fields.ToArray()),
        };
    }

    /// <summary>Prowlarr's <c>SonarrIndexer.Equals</c>: if this fails, Prowlarr would push an update on every sync.</summary>
    private static void AssertProwlarrEquals(JsonObject expected, JsonObject remote)
    {
        foreach (var key in new[] { "id", "enableRss", "enableAutomaticSearch", "enableInteractiveSearch", "name", "implementation", "priority" })
        {
            Assert.That(remote[key]?.ToJsonString(), Is.EqualTo(expected[key]?.ToJsonString()), key);
        }

        foreach (var name in new[] { "baseUrl", "apiPath", "categories", "animeCategories", "animeStandardFormatSearch", "minimumSeeders", "seedCriteria.seedRatio", "seedCriteria.seedTime", "seedCriteria.seasonPackSeedTime", "rejectBlocklistedTorrentHashesWhileGrabbing" })
        {
            Assert.That(Field(remote, name)?.ToJsonString(), Is.EqualTo(Field(expected, name)?.ToJsonString()), name);
        }

        Assert.That((string)Field(remote, "apiKey")!, Is.EqualTo("********").Or.EqualTo("prowlarr-key"));
    }

    private static JsonNode? Field(JsonObject indexer, string name) =>
        indexer["fields"]!.AsArray().Single(f => (string)f!["name"]! == name)!["value"];

    private sealed class CapsHandler : HttpMessageHandler
    {
        public bool Down { get; set; }

        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Urls)
            {
                Urls.Add(request.RequestUri!.AbsoluteUri);
            }

            return Task.FromResult(Down
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Caps, Encoding.UTF8, "application/xml") });
        }
    }
}
