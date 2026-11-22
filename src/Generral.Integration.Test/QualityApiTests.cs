using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Generral.Integration.Test;

public class QualityApiTests
{
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
    public async Task Default_profiles_and_definitions_are_seeded()
    {
        var profiles = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/qualityprofile"))!;
        var definitions = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/qualitydefinition"))!;
        var hd1080 = profiles.Single(p => (string)p!["name"]! == "HD-1080p")!;
        var webdl1080 = definitions.Single(d => (string)d!["title"]! == "WEBDL-1080p")!;

        Assert.Multiple(() =>
        {
            Assert.That(profiles.Select(p => (string)p!["name"]!), Is.EqualTo(new[] { "Any", "SD", "HD-720p", "HD-1080p", "Ultra-HD", "HD - 720p/1080p" }));
            Assert.That((int)hd1080["cutoff"]!, Is.EqualTo(9), "HDTV-1080p");
            Assert.That(hd1080["items"]!.AsArray().Single(i => (string?)i!["name"] == "WEB 1080p")!["allowed"]!.GetValue<bool>(), Is.True);
            Assert.That(definitions, Has.Count.EqualTo(22));
            Assert.That((int)webdl1080["quality"]!["id"]!, Is.EqualTo(3));
            Assert.That((string)webdl1080["quality"]!["source"]!, Is.EqualTo("web"));
            Assert.That((double)webdl1080["maxSize"]!, Is.EqualTo(250));
        });
    }

    [Test]
    public async Task Quality_profiles_can_be_created_from_the_schema_updated_and_deleted()
    {
        var schema = (await _client.GetFromJsonAsync<JsonObject>("/api/v1/qualityprofile/schema"))!;
        schema["name"] = "Sport 1080p";
        var invalid = await _client.PostAsJsonAsync("/api/v1/qualityprofile", schema);

        var webdl = schema["items"]!.AsArray().Single(i => (string?)i!["name"] == "WEB 1080p")!;
        webdl["allowed"] = true;
        schema["cutoff"] = (int)webdl["id"]!;
        var created = await _client.PostAsJsonAsync("/api/v1/qualityprofile", schema);
        var profile = (await created.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = (int)profile["id"]!;

        profile["upgradeAllowed"] = true;
        var updated = await _client.PutAsJsonAsync($"/api/v1/qualityprofile/{id}", profile);
        var stored = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/qualityprofile/{id}"))!;
        var deleted = await _client.DeleteAsync($"/api/v1/qualityprofile/{id}");

        Assert.Multiple(async () =>
        {
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
            Assert.That(updated.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(stored["upgradeAllowed"]!.GetValue<bool>(), Is.True);
            Assert.That((int)stored["cutoff"]!, Is.EqualTo((int)webdl["id"]!));
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        });
    }

    [Test]
    public async Task Series_get_the_default_profile_validate_it_and_block_its_deletion()
    {
        var series = await _client.PostAsJsonAsync("/api/v1/series", new JsonObject { ["title"] = "UFC", ["path"] = "/tv/UFC" });
        var unknown = await _client.PostAsJsonAsync("/api/v1/series", new JsonObject { ["title"] = "F1", ["path"] = "/tv/F1", ["qualityProfileId"] = 999 });
        var profileId = (int)(await series.Content.ReadFromJsonAsync<JsonObject>())!["qualityProfileId"]!;
        var delete = await _client.DeleteAsync($"/api/v1/qualityprofile/{profileId}");

        Assert.Multiple(async () =>
        {
            Assert.That(series.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(profileId, Is.EqualTo(1));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await unknown.Content.ReadAsStringAsync(), Does.Contain("qualityProfileId"));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await delete.Content.ReadAsStringAsync(), Does.Contain("in use"));
        });
    }

    [Test]
    public async Task Quality_definitions_update_one_or_many_and_copy_sizes_into_profiles()
    {
        var definitions = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/qualitydefinition"))!;
        var webdl = definitions.Single(d => (int)d!["quality"]!["id"]! == 3)!.AsObject();
        webdl["maxSize"] = 400;
        var single = await _client.PutAsJsonAsync($"/api/v1/qualitydefinition/{(int)webdl["id"]!}", webdl);

        webdl["title"] = "WEB-DL 1080p";
        webdl["minSize"] = 500;
        var invalid = await _client.PutAsJsonAsync("/api/v1/qualitydefinition/update", new JsonArray(webdl.DeepClone()));
        webdl["minSize"] = 5;
        var many = await _client.PutAsJsonAsync("/api/v1/qualitydefinition/update", new JsonArray(webdl.DeepClone()));

        var profile = (await _client.GetFromJsonAsync<JsonObject>("/api/v1/qualityprofile/1"))!;
        var item = profile["items"]!.AsArray().SelectMany(i => i!["items"]!.AsArray()).Single(i => (int)i!["quality"]!["id"]! == 3)!;
        var stored = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/qualitydefinition/{(int)webdl["id"]!}"))!;

        Assert.Multiple(() =>
        {
            Assert.That(single.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(many.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That((string)stored["title"]!, Is.EqualTo("WEB-DL 1080p"));
            Assert.That((double)item["maxSize"]!, Is.EqualTo(400));
            Assert.That((double)item["minSize"]!, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task Release_profiles_crud_and_validation()
    {
        var empty = await _client.PostAsJsonAsync("/api/v1/releaseprofile", new JsonObject { ["name"] = "Nothing" });
        var badIndexer = await _client.PostAsJsonAsync("/api/v1/releaseprofile", new JsonObject { ["required"] = "SKY", ["indexerId"] = 42 });
        var created = await _client.PostAsJsonAsync("/api/v1/releaseprofile", new JsonObject
        {
            ["name"] = "Sky",
            ["required"] = "Sky Sports, SKY",
            ["ignored"] = new JsonArray("/\\bFrench\\b/i"),
        });
        var profile = (await created.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = (int)profile["id"]!;
        profile["enabled"] = false;
        var updated = await _client.PutAsJsonAsync($"/api/v1/releaseprofile/{id}", profile);
        var stored = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/releaseprofile/{id}"))!;
        var deleted = await _client.DeleteAsync($"/api/v1/releaseprofile/{id}");
        var missing = await _client.GetAsync($"/api/v1/releaseprofile/{id}");

        Assert.Multiple(() =>
        {
            Assert.That(empty.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(badIndexer.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(profile["required"]!.AsArray().Select(t => (string)t!), Is.EqualTo(new[] { "Sky Sports", "SKY" }));
            Assert.That(updated.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(stored["enabled"]!.GetValue<bool>(), Is.False);
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }
}
