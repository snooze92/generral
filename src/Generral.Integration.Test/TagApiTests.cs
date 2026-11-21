using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Integration.Test;

public class TagApiTests
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
    public async Task Tags_are_lowercased_listed_by_label_and_reused_on_duplicate_labels()
    {
        var sport = await CreateTagAsync("Sport");
        await CreateTagAsync("football");
        var again = await CreateTagAsync("SPORT");

        var all = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/tag"))!;
        var single = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/tag/{(int)sport["id"]!}"))!;
        var missing = await _client.GetAsync("/api/v1/tag/999");

        Assert.Multiple(() =>
        {
            Assert.That((string)sport["label"]!, Is.EqualTo("sport"));
            Assert.That((int)again["id"]!, Is.EqualTo((int)sport["id"]!));
            Assert.That(all.Select(t => (string)t!["label"]!), Is.EqualTo(new[] { "football", "sport" }));
            Assert.That((string)single["label"]!, Is.EqualTo("sport"));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task Invalid_labels_are_rejected()
    {
        await CreateTagAsync("sport");
        var football = await CreateTagAsync("football");

        var empty = await _client.PostAsJsonAsync("/api/v1/tag", new JsonObject { ["label"] = "" });
        var spaces = await _client.PostAsJsonAsync("/api/v1/tag", new JsonObject { ["label"] = "two words" });
        var duplicate = await _client.PutAsJsonAsync($"/api/v1/tag/{(int)football["id"]!}", new JsonObject { ["label"] = "Sport" });
        var failure = (await spaces.Content.ReadFromJsonAsync<JsonArray>())![0]!;

        Assert.Multiple(() =>
        {
            Assert.That(empty.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(spaces.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((string)failure["propertyName"]!, Is.EqualTo("label"));
            Assert.That((string)failure["errorMessage"]!, Is.EqualTo("Allowed characters a-z, 0-9 and -"));
            Assert.That(duplicate.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        });
    }

    [Test]
    public async Task Tags_can_be_renamed()
    {
        var id = (int)(await CreateTagAsync("sport"))["id"]!;

        var update = await _client.PutAsJsonAsync($"/api/v1/tag/{id}", new JsonObject { ["id"] = id, ["label"] = "Motor-Sport" });
        var updated = (await update.Content.ReadFromJsonAsync<JsonObject>())!;
        var missing = await _client.PutAsJsonAsync("/api/v1/tag/999", new JsonObject { ["label"] = "x" });

        Assert.Multiple(() =>
        {
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That((string)updated["label"]!, Is.EqualTo("motor-sport"));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task Details_list_the_sources_and_series_using_each_tag_and_delete_removes_the_tag_from_them()
    {
        var sport = (int)(await CreateTagAsync("sport"))["id"]!;
        var unused = (int)(await CreateTagAsync("unused"))["id"]!;
        var sourceId = (int)(await CreateSourceAsync("Cup", sport))["id"]!;
        int seriesId;
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
            var series = new Series { Title = "Cup", SortTitle = "cup", CleanTitle = "cup", TitleSlug = "cup", Path = "/tv/Cup", RootFolderPath = "/tv", Tags = [sport, unused] };
            db.Series.Add(series);
            await db.SaveChangesAsync();
            seriesId = series.Id;
        }

        var details = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/tag/detail"))!;
        var detail = details.Single(d => (int)d!["id"]! == sport)!;

        var delete = await _client.DeleteAsync($"/api/v1/tag/{sport}");
        var source = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/source/{sourceId}"))!;
        List<int> seriesTags;
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
            seriesTags = (await db.Series.SingleAsync(s => s.Id == seriesId)).Tags;
        }

        var afterDelete = await _client.GetAsync($"/api/v1/tag/{sport}");
        var deleteMissing = await _client.DeleteAsync($"/api/v1/tag/{sport}");

        Assert.Multiple(() =>
        {
            Assert.That((string)detail["label"]!, Is.EqualTo("sport"));
            Assert.That(detail["sourceIds"]!.AsArray().Select(i => (int)i!), Is.EqualTo(new[] { sourceId }));
            Assert.That(detail["seriesIds"]!.AsArray().Select(i => (int)i!), Is.EqualTo(new[] { seriesId }));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(source["tags"]!.AsArray(), Is.Empty);
            Assert.That(seriesTags, Is.EqualTo(new[] { unused }));
            Assert.That(afterDelete.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(deleteMissing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task Sources_accept_existing_tag_ids_only()
    {
        var sport = (int)(await CreateTagAsync("sport"))["id"]!;

        var created = await CreateSourceAsync("Cup", sport, sport);
        var invalid = await _client.PostAsJsonAsync("/api/v1/source", Source("Other", sport, 999));
        var failures = (await invalid.Content.ReadFromJsonAsync<JsonArray>())!;

        Assert.Multiple(() =>
        {
            Assert.That(created["tags"]!.AsArray().Select(i => (int)i!), Is.EqualTo(new[] { sport }));
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(failures.Select(f => (string)f!["propertyName"]!), Is.EqualTo(new[] { "tags" }));
            Assert.That((string)failures[0]!["errorMessage"]!, Does.Contain("999"));
        });
    }

    private async Task<JsonObject> CreateTagAsync(string label)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/tag", new JsonObject { ["label"] = label });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private async Task<JsonObject> CreateSourceAsync(string name, params int[] tags)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/source", Source(name, tags));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private static JsonObject Source(string name, params int[] tags) => new()
    {
        ["name"] = name,
        ["type"] = "static",
        ["tags"] = new JsonArray(tags.Select(t => (JsonNode)t).ToArray()),
        ["fields"] = new JsonArray(
            new JsonObject { ["name"] = "format", ["value"] = "yaml" },
            new JsonObject { ["name"] = "events", ["value"] = "- key: final\n  title: Final\n" }),
    };
}
