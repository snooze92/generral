using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Mappings;
using Generral.Core.Sources;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Integration.Test;

public class SeriesApiTests
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
    public async Task A_series_is_created_with_derived_titles_path_and_seasons()
    {
        var created = await CreateSeriesAsync(new JsonObject
        {
            ["title"] = "The Cup: Finals",
            ["year"] = 2026,
            ["rootFolderPath"] = "/tv/",
            ["seriesType"] = "daily",
            ["monitorNewItems"] = "none",
            ["genres"] = new JsonArray("Sport"),
            ["images"] = new JsonArray(new JsonObject { ["coverType"] = "poster", ["remoteUrl"] = "https://img/p.jpg" }),
            ["seasons"] = new JsonArray(new JsonObject { ["seasonNumber"] = 2026, ["title"] = "2026", ["monitored"] = false }),
        });

        var fetched = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/series/{(int)created["id"]!}"))!;
        var all = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/series"))!;
        var season = fetched["seasons"]![0]!;

        Assert.Multiple(() =>
        {
            Assert.That((string)fetched["title"]!, Is.EqualTo("The Cup: Finals"));
            Assert.That((string)fetched["sortTitle"]!, Is.EqualTo("cup finals"));
            Assert.That((string)fetched["cleanTitle"]!, Is.EqualTo("thecupfinals"));
            Assert.That((string)fetched["titleSlug"]!, Is.EqualTo("the-cup-finals"));
            Assert.That((string)fetched["path"]!, Is.EqualTo("/tv/The Cup - Finals (2026)"));
            Assert.That((string)fetched["rootFolderPath"]!, Is.EqualTo("/tv"));
            Assert.That((string)fetched["seriesType"]!, Is.EqualTo("daily"));
            Assert.That((string)fetched["monitorNewItems"]!, Is.EqualTo("none"));
            Assert.That((string)fetched["guid"]!, Does.StartWith("generral://series/"));
            Assert.That((string)fetched["images"]![0]!["coverType"]!, Is.EqualTo("poster"));
            Assert.That((string)fetched["images"]![0]!["remoteUrl"]!, Is.EqualTo("https://img/p.jpg"));
            Assert.That((int)season["seasonNumber"]!, Is.EqualTo(2026));
            Assert.That((string)season["title"]!, Is.EqualTo("2026"));
            Assert.That((bool)season["monitored"]!, Is.False);
            Assert.That((int)season["statistics"]!["totalEpisodeCount"]!, Is.EqualTo(0));
            Assert.That((int)fetched["statistics"]!["seasonCount"]!, Is.EqualTo(1));
            Assert.That((decimal)fetched["statistics"]!["percentOfEpisodes"]!, Is.EqualTo(0));
            Assert.That(all.Select(s => (int)s!["id"]!), Is.EqualTo(new[] { (int)created["id"]! }));
        });
    }

    [Test]
    public async Task Invalid_series_are_rejected_with_validation_failures()
    {
        await CreateSeriesAsync(Series("Cup"));

        async Task<string[]> Failures(JsonObject resource)
        {
            var response = await _client.PostAsJsonAsync("/api/v1/series", resource);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            return (await response.Content.ReadFromJsonAsync<JsonArray>())!.Select(f => (string)f!["propertyName"]!).ToArray();
        }

        var duplicate = await Failures(Series("CUP"));
        var missing = await Failures(new JsonObject { ["title"] = " " });
        var invalid = await Failures(new JsonObject
        {
            ["title"] = "Other",
            ["path"] = "relative/path",
            ["titleSlug"] = "Not A Slug",
            ["tags"] = new JsonArray(999),
            ["seasons"] = new JsonArray(new JsonObject { ["seasonNumber"] = 1 }, new JsonObject { ["seasonNumber"] = 1 }),
        });
        var takenPathAndSlug = await Failures(new JsonObject { ["title"] = "Other", ["path"] = "/tv/Cup", ["titleSlug"] = "cup" });

        Assert.Multiple(() =>
        {
            Assert.That(duplicate, Is.EqualTo(new[] { "title" }));
            Assert.That(missing, Is.EqualTo(new[] { "title", "path" }));
            Assert.That(invalid, Is.EquivalentTo(new[] { "titleSlug", "path", "seasons", "tags" }));
            Assert.That(takenPathAndSlug, Is.EquivalentTo(new[] { "titleSlug", "path" }));
        });
    }

    [Test]
    public async Task Updating_a_series_applies_changes_and_season_monitoring_to_its_episodes()
    {
        var seed = await SeedAsync();
        var series = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/series/{seed.SeriesId}"))!;
        var sport = (int)(await (await _client.PostAsJsonAsync("/api/v1/tag", new JsonObject { ["label"] = "sport" })).Content.ReadFromJsonAsync<JsonObject>())!["id"]!;

        series["title"] = "Cup Renamed";
        series["monitored"] = false;
        series["tags"] = new JsonArray(sport);
        series["seasons"]![0]!["monitored"] = false;
        var response = await _client.PutAsJsonAsync($"/api/v1/series/{seed.SeriesId}", series);
        var updated = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var episodes = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/episode?seriesId={seed.SeriesId}"))!;

        series["title"] = "Other";
        var conflict = await _client.PutAsJsonAsync($"/api/v1/series/{seed.SeriesId}", new JsonObject { ["title"] = "x", ["path"] = "" });
        var missing = await _client.PutAsJsonAsync("/api/v1/series/999", series);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That((string)updated["title"]!, Is.EqualTo("Cup Renamed"));
            Assert.That((string)updated["sortTitle"]!, Is.EqualTo("cup renamed"));
            Assert.That((string)updated["titleSlug"]!, Is.EqualTo("cup"), "the slug is kept");
            Assert.That((bool)updated["monitored"]!, Is.False);
            Assert.That(updated["tags"]!.AsArray().Select(t => (int)t!), Is.EqualTo(new[] { sport }));
            Assert.That((string)updated["images"]![0]!["remoteUrl"]!, Is.EqualTo("https://img/p.jpg"), "images are kept");
            Assert.That((bool)updated["seasons"]![0]!["monitored"]!, Is.False);
            Assert.That(episodes.Select(e => (bool)e!["monitored"]!), Is.All.False);
            Assert.That(conflict.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task Statistics_count_episodes_like_Sonarr()
    {
        var seed = await SeedAsync();

        var series = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/series/{seed.SeriesId}"))!;
        var stats = series["statistics"]!;
        var season = series["seasons"]![0]!["statistics"]!;

        Assert.Multiple(() =>
        {
            // Aired + upcoming; the orphaned episode without a file is not counted.
            Assert.That((int)stats["totalEpisodeCount"]!, Is.EqualTo(2));
            Assert.That((int)stats["episodeCount"]!, Is.EqualTo(1));
            Assert.That((int)stats["episodeFileCount"]!, Is.EqualTo(0));
            Assert.That((long)stats["sizeOnDisk"]!, Is.EqualTo(0));
            Assert.That((int)stats["seasonCount"]!, Is.EqualTo(1));
            Assert.That((int)season["totalEpisodeCount"]!, Is.EqualTo(2));
            Assert.That((DateTime)series["nextAiring"]!, Is.EqualTo(Future));
            Assert.That((DateTime)series["previousAiring"]!, Is.EqualTo(Past));
        });
    }

    [Test]
    public async Task Deleting_a_series_cascades_to_seasons_episodes_mappings_and_links()
    {
        var seed = await SeedAsync();
        var other = await CreateSeriesAsync(Series("Other"));

        var delete = await _client.DeleteAsync($"/api/v1/series/{seed.SeriesId}");
        var get = await _client.GetAsync($"/api/v1/series/{seed.SeriesId}");
        var again = await _client.DeleteAsync($"/api/v1/series/{seed.SeriesId}");

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        Assert.Multiple(async () =>
        {
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(await db.Series.Select(s => s.Id).ToListAsync(), Is.EqualTo(new[] { (int)other["id"]! }));
            Assert.That(await db.Seasons.CountAsync(), Is.Zero);
            Assert.That(await db.Episodes.CountAsync(), Is.Zero);
            Assert.That(await db.Mappings.CountAsync(), Is.Zero);
            Assert.That(await db.MappingVersions.CountAsync(), Is.Zero);
            Assert.That(await db.EpisodeLinks.CountAsync(), Is.Zero);
            Assert.That(await db.Events.CountAsync(), Is.EqualTo(3), "events belong to the source");
        });
    }

    [Test]
    public async Task Episodes_are_listed_by_series_and_season_with_their_links()
    {
        var seed = await SeedAsync();

        var all = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/episode?seriesId={seed.SeriesId}"))!;
        var withOrphans = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/episode?seriesId={seed.SeriesId}&includeOrphaned=true"))!;
        var otherSeason = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/episode?seriesId={seed.SeriesId}&seasonNumber=2025"))!;
        var byIds = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/episode?episodeIds={seed.EpisodeIds[1]}&episodeIds={seed.EpisodeIds[0]}"))!;
        var noSeries = await _client.GetAsync("/api/v1/episode");
        var one = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/episode/{seed.EpisodeIds[0]}"))!;
        var missing = await _client.GetAsync("/api/v1/episode/999");

        Assert.Multiple(() =>
        {
            Assert.That(all.Select(e => (int)e!["episodeNumber"]!), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(withOrphans.Select(e => (string)e!["status"]!), Is.EqualTo(new[] { "active", "active", "orphaned" }));
            Assert.That(otherSeason, Is.Empty);
            Assert.That(byIds.Select(e => (int)e!["id"]!), Is.EqualTo(new[] { seed.EpisodeIds[0], seed.EpisodeIds[1] }));
            Assert.That(noSeries.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((int)one["seriesId"]!, Is.EqualTo(seed.SeriesId));
            Assert.That((int)one["seasonNumber"]!, Is.EqualTo(2026));
            Assert.That((string)one["title"]!, Is.EqualTo("Semi-final"));
            Assert.That((string)one["airDate"]!, Is.EqualTo("2026-01-10"));
            Assert.That((DateTime)one["airDateUtc"]!, Is.EqualTo(Past));
            Assert.That((int)one["absoluteEpisodeNumber"]!, Is.EqualTo(7));
            Assert.That((int)one["runtime"]!, Is.EqualTo(120));
            Assert.That((bool)one["hasFile"]!, Is.False);
            Assert.That((bool)one["unverifiedSceneNumbering"]!, Is.False);
            Assert.That((bool)one["numberLocked"]!, Is.True);
            Assert.That((string)one["source"]!, Is.EqualTo("mapping"));
            Assert.That((string)one["images"]![0]!["coverType"]!, Is.EqualTo("screenshot"));
            Assert.That((int)one["links"]![0]!["eventId"]!, Is.EqualTo(seed.EventIds[0]));
            Assert.That((int)one["links"]![0]!["mappingId"]!, Is.EqualTo(seed.MappingId));
            Assert.That(one["links"]![0]!["partKey"], Is.Null);
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task Episode_monitoring_and_lock_can_be_changed()
    {
        var seed = await SeedAsync();
        var episode = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/episode/{seed.EpisodeIds[0]}"))!;

        episode["monitored"] = false;
        episode["title"] = "ignored";
        var put = await _client.PutAsJsonAsync($"/api/v1/episode/{seed.EpisodeIds[0]}", episode);
        var single = (await put.Content.ReadFromJsonAsync<JsonObject>())!;

        var monitor = await _client.PutAsJsonAsync("/api/v1/episode/monitor", new JsonObject { ["episodeIds"] = new JsonArray(seed.EpisodeIds[1], seed.EpisodeIds[2]), ["monitored"] = false });
        var monitored = (await monitor.Content.ReadFromJsonAsync<JsonArray>())!;
        var emptyMonitor = await _client.PutAsJsonAsync("/api/v1/episode/monitor", new JsonObject { ["episodeIds"] = new JsonArray(), ["monitored"] = true });

        var unlock = await _client.PutAsJsonAsync($"/api/v1/episode/{seed.EpisodeIds[0]}/lock", new JsonObject { ["numberLocked"] = false });
        var unlocked = (await unlock.Content.ReadFromJsonAsync<JsonObject>())!;
        var lockMissing = await _client.PutAsJsonAsync("/api/v1/episode/999/lock", new JsonObject { ["numberLocked"] = true });
        var putMissing = await _client.PutAsJsonAsync("/api/v1/episode/999", episode);

        using var scope = _host.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<GenerralDbContext>().Episodes.OrderBy(e => e.Id).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That((bool)single["monitored"]!, Is.False);
            Assert.That((string)single["title"]!, Is.EqualTo("Semi-final"));
            Assert.That(monitor.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(monitored.Select(e => (bool)e!["monitored"]!), Is.EqualTo(new[] { false, false }));
            Assert.That(emptyMonitor.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(unlock.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That((bool)unlocked["numberLocked"]!, Is.False);
            Assert.That(lockMissing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(putMissing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(stored.Select(e => e.Monitored), Is.All.False);
            Assert.That(stored[0].NumberLocked, Is.False);
            Assert.That(stored[0].Title, Is.EqualTo("Semi-final"));
        });
    }

    private static readonly DateTime Past = new(2026, 1, 10, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Future = new(2099, 1, 1, 15, 0, 0, DateTimeKind.Utc);

    private sealed record Seed(int SeriesId, int MappingId, int[] EventIds, int[] EpisodeIds);

    /// <summary>Series "Cup" (season 2026) with an aired, an upcoming and an orphaned episode, each linked to an event.</summary>
    private async Task<Seed> SeedAsync()
    {
        var created = await CreateSeriesAsync(Series("Cup", new JsonObject { ["seasonNumber"] = 2026 }));
        var seriesId = (int)created["id"]!;

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var source = new Source { Name = "Cup source", Type = "static" };
        var events = new[] { "semi", "final", "gone" }.Select(k => new Event { Source = source, Key = k, Title = k, Hash = k }).ToArray();
        var mapping = new Mapping { Name = "Cup", SeriesId = seriesId, Source = source };
        db.MappingVersions.Add(new MappingVersion { Mapping = mapping, Version = 1 });
        var episodes = new[]
        {
            new Episode
            {
                SeriesId = seriesId, SeasonNumber = 2026, EpisodeNumber = 1, AbsoluteEpisodeNumber = 7, Title = "Semi-final", AirDateUtc = Past,
                AirDate = new DateOnly(2026, 1, 10), Runtime = 120, NumberLocked = true, Images = new ImageSet { Thumb = "https://img/t.jpg" },
            },
            new Episode { SeriesId = seriesId, SeasonNumber = 2026, EpisodeNumber = 2, Title = "Final", AirDateUtc = Future },
            new Episode { SeriesId = seriesId, SeasonNumber = 2026, EpisodeNumber = 3, Title = "Gone", Status = EpisodeStatus.Orphaned },
        };
        db.EpisodeLinks.AddRange(episodes.Select((e, i) => new EpisodeLink { Mapping = mapping, Event = events[i], Episode = e }));
        await db.SaveChangesAsync();

        return new Seed(seriesId, mapping.Id, events.Select(e => e.Id).ToArray(), episodes.Select(e => e.Id).ToArray());
    }

    private async Task<JsonObject> CreateSeriesAsync(JsonObject resource)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/series", resource);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private static JsonObject Series(string title, params JsonObject[] seasons) => new()
    {
        ["title"] = title,
        ["path"] = $"/tv/{title}",
        ["images"] = new JsonArray(new JsonObject { ["coverType"] = "poster", ["url"] = "https://img/p.jpg" }),
        ["seasons"] = new JsonArray(seasons),
    };
}
