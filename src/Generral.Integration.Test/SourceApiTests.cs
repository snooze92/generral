using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Mappings;
using Generral.Core.Tv;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Integration.Test;

public class SourceApiTests
{
    private const string Events = """
        - key: final
          title: Final
          start: 2027-11-13T15:00:00Z
        - key: semi
          title: Semi-final
          start: 2027-11-10T15:00:00Z
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
    public async Task Schema_lists_the_source_types_with_default_fields()
    {
        var schema = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/source/schema"))!;

        var thesportsdb = schema.Single(s => (string)s!["type"]! == "thesportsdb")!;
        var apiKey = thesportsdb["fields"]!.AsArray().Single(f => (string)f!["name"]! == "apiKey")!;
        Assert.Multiple(() =>
        {
            Assert.That(schema.Select(s => (string)s!["type"]!), Is.EquivalentTo(new[] { "thesportsdb", "http-json", "http-html", "ical", "static" }));
            Assert.That((string)apiKey["value"]!, Is.EqualTo("123"));
            Assert.That((string)apiKey["type"]!, Is.EqualTo("password"));
            Assert.That((int)thesportsdb["refreshInterval"]!, Is.EqualTo(720));
        });
    }

    [Test]
    public async Task A_static_source_is_refreshed_through_a_command_and_its_events_listed()
    {
        var created = await CreateStaticSourceAsync("Cup");
        var id = (int)created["id"]!;

        var refresh = await _client.PostAsync($"/api/v1/source/{id}/refresh", null);
        Assert.That(refresh.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var command = await WaitForCommandAsync((await refresh.Content.ReadFromJsonAsync<JsonObject>())!);

        var events = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/source/{id}/events"))!;
        var source = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/source/{id}"))!;
        var ev = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/event/{(int)events[0]!["id"]!}"))!;

        Assert.Multiple(() =>
        {
            Assert.That((string)command["status"]!, Is.EqualTo("completed"), (string?)command["message"]);
            Assert.That((string)command["name"]!, Is.EqualTo("RefreshSource"));
            Assert.That((int)command["body"]!["sourceId"]!, Is.EqualTo(id));
            Assert.That(events.Select(e => (string)e!["key"]!), Is.EqualTo(new[] { "semi", "final" }));
            Assert.That((int)source["eventCount"]!, Is.EqualTo(2));
            Assert.That(source["lastSuccess"], Is.Not.Null);
            Assert.That(source["nextRefresh"], Is.Not.Null);
            Assert.That((string)ev["title"]!, Is.EqualTo("Semi-final"));
            Assert.That(ev["revisions"]!.AsArray(), Is.Empty);
        });
    }

    [Test]
    public async Task Refresh_sources_command_can_be_posted_and_listed()
    {
        for (var i = 1; i <= 5; i++)
        {
            await CreateStaticSourceAsync($"Cup {i}");
        }

        var response = await _client.PostAsJsonAsync("/api/v1/command", new { name = "RefreshSources", force = true });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var command = await WaitForCommandAsync((await response.Content.ReadFromJsonAsync<JsonObject>())!);
        var all = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/command"))!;

        Assert.Multiple(() =>
        {
            Assert.That((string)command["status"]!, Is.EqualTo("completed"));
            Assert.That((string)command["message"]!, Is.EqualTo("Refreshed 5 source(s)"));
            Assert.That((string)command["trigger"]!, Is.EqualTo("manual"));
            Assert.That(all.Select(c => (int)c!["id"]!), Does.Contain((int)command["id"]!));
        });
    }

    [Test]
    public async Task Unknown_commands_are_rejected()
    {
        var unknown = await _client.PostAsJsonAsync("/api/v1/command", new { name = "Nope" });
        var noSource = await _client.PostAsJsonAsync("/api/v1/command", new { name = "RefreshSource" });

        Assert.Multiple(() =>
        {
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(noSource.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        });
    }

    [Test]
    public async Task Test_previews_an_unsaved_definition_without_storing_it()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/source/test", StaticSource("Preview", Events + "\n- title: No key"));
        var result = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var sources = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/source"))!;

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((bool)result["success"]!, Is.True);
            Assert.That(result["events"]!.AsArray().Select(e => (string)e!["key"]!), Is.EqualTo(new[] { "final", "semi" }));
            Assert.That(result["itemErrors"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(result["responses"]!.AsArray(), Is.Empty);
            Assert.That(sources, Is.Empty);
        });
    }

    [Test]
    public async Task Test_reports_a_failed_fetch()
    {
        var resource = new JsonObject
        {
            ["name"] = "Calendar",
            ["type"] = "ical",
            ["fields"] = new JsonArray(new JsonObject { ["name"] = "url", ["value"] = "http://169.254.169.254/calendar.ics" }),
        };

        var result = (await (await _client.PostAsJsonAsync("/api/v1/source/test", resource)).Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Multiple(() =>
        {
            Assert.That((bool)result["success"]!, Is.False);
            Assert.That((string)result["error"]!, Is.Not.Empty);
        });
    }

    [Test]
    public async Task Sources_can_be_updated_and_deleted()
    {
        var id = (int)(await CreateStaticSourceAsync("Cup"))["id"]!;
        var resource = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/source/{id}"))!;
        resource["name"] = "World Cup";
        resource["refreshInterval"] = 60;

        var update = await _client.PutAsJsonAsync($"/api/v1/source/{id}", resource);
        var updated = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/source/{id}"))!;
        var delete = await _client.DeleteAsync($"/api/v1/source/{id}");
        var afterDelete = await _client.GetAsync($"/api/v1/source/{id}");

        Assert.Multiple(() =>
        {
            Assert.That(update.IsSuccessStatusCode, Is.True);
            Assert.That((string)updated["name"]!, Is.EqualTo("World Cup"));
            Assert.That((int)updated["refreshInterval"]!, Is.EqualTo(60));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(afterDelete.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task Stored_passwords_are_masked_and_kept_when_sent_back_masked()
    {
        var resource = new JsonObject
        {
            ["name"] = "UFC",
            ["type"] = "thesportsdb",
            ["fields"] = new JsonArray(
                new JsonObject { ["name"] = "apiKey", ["value"] = "premiumKey42" },
                new JsonObject { ["name"] = "leagueId", ["value"] = "4443" }),
        };
        var response = await _client.PostAsJsonAsync("/api/v1/source", resource);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = (int)created["id"]!;

        (await _client.PutAsJsonAsync($"/api/v1/source/{id}", created)).EnsureSuccessStatusCode();

        using var scope = _host.Services.CreateScope();
        var stored = scope.ServiceProvider.GetRequiredService<GenerralDbContext>().Sources.Single();
        Assert.Multiple(() =>
        {
            Assert.That((string)Field(created, "apiKey")["value"]!, Is.EqualTo("********"));
            Assert.That((string)stored.Settings["apiKey"]!, Is.EqualTo("premiumKey42"));
        });
    }

    [Test]
    public async Task Delete_is_rejected_while_a_mapping_uses_the_source()
    {
        var id = (int)(await CreateStaticSourceAsync("Cup"))["id"]!;
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
            var series = new Series { Title = "Cup", SortTitle = "cup", CleanTitle = "cup", TitleSlug = "cup", Path = "/tv/Cup", RootFolderPath = "/tv" };
            db.Mappings.Add(new Mapping { Name = "Cup mapping", Series = series, SourceId = id });
            await db.SaveChangesAsync();
        }

        var response = await _client.DeleteAsync($"/api/v1/source/{id}");
        var failures = (await response.Content.ReadFromJsonAsync<JsonArray>())!;

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((string)failures[0]!["errorMessage"]!, Does.Contain("Cup mapping"));
        });
    }

    [Test]
    public async Task Invalid_sources_are_rejected_with_validation_failures()
    {
        await CreateStaticSourceAsync("Cup");

        async Task<string[]> Failures(JsonObject resource)
        {
            var response = await _client.PostAsJsonAsync("/api/v1/source", resource);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            return (await response.Content.ReadFromJsonAsync<JsonArray>())!.Select(f => (string)f!["propertyName"]!).ToArray();
        }

        var duplicate = await Failures(StaticSource("cup", Events));
        var unknownType = await Failures(new JsonObject { ["name"] = "Other", ["type"] = "nope" });
        var tooOften = StaticSource("Other", Events);
        tooOften["refreshInterval"] = 5;
        var interval = await Failures(tooOften);
        var invalidSettings = await Failures(StaticSource("Other", "- [not: valid"));

        Assert.Multiple(() =>
        {
            Assert.That(duplicate, Is.EqualTo(new[] { "name" }));
            Assert.That(unknownType, Is.EqualTo(new[] { "type" }));
            Assert.That(interval, Is.EqualTo(new[] { "refreshInterval" }));
            Assert.That(invalidSettings, Is.EqualTo(new[] { "events" }));
        });
    }

    private static JsonObject Field(JsonObject resource, string name) =>
        resource["fields"]!.AsArray().Single(f => (string)f!["name"]! == name)!.AsObject();

    private static JsonObject StaticSource(string name, string events) => new()
    {
        ["name"] = name,
        ["type"] = "static",
        ["fields"] = new JsonArray(
            new JsonObject { ["name"] = "format", ["value"] = "yaml" },
            new JsonObject { ["name"] = "events", ["value"] = events }),
    };

    private async Task<JsonObject> CreateStaticSourceAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/source", StaticSource(name, Events));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private async Task<JsonObject> WaitForCommandAsync(JsonObject command)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            command = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/command/{(int)command["id"]!}"))!;
            if ((string)command["status"]! is "completed" or "failed" || DateTime.UtcNow > deadline)
            {
                return command;
            }

            await Task.Delay(50);
        }
    }
}
