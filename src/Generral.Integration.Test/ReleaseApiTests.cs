using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.DecisionEngine;
using Generral.Core.Download;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Generral.Integration.Test;

public class ReleaseApiTests
{
    private readonly RecordingDownloadService _grabs = new();
    private TorznabHandler _handler = null!;
    private GenerralTestHost _host = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _handler = new TorznabHandler();
        _grabs.Grabs.Clear();
        _host = await GenerralTestHost.StartAsync(services: services =>
        {
            services.PostConfigure<HttpClientFactoryOptions>(TorznabClient.ClientName,
                o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = _handler));
            services.AddSingleton<IDownloadService>(_grabs);
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
    public async Task Episode_search_grabs_the_best_accepted_release_per_part()
    {
        var (prelimsId, mainCardId) = await SeedAsync();

        var command = await _client.PostAsJsonAsync("/api/v1/command", new JsonObject
        {
            ["name"] = "EpisodeSearch",
            ["episodeIds"] = new JsonArray(prelimsId, mainCardId),
        });
        var finished = await WaitAsync((int)(await command.Content.ReadFromJsonAsync<JsonObject>())!["id"]!);
        var grabs = _grabs.Grabs;
        var searched = await _host.Services.CreateScope().ServiceProvider.GetRequiredService<GenerralDbContext>().Episodes.Where(e => e.LastSearchTime != null).CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That((string)finished["status"]!, Is.EqualTo("completed"), (string?)finished["message"]);
            Assert.That((string)finished["message"]!, Is.EqualTo("2 episode(s) searched, 5 release(s) found, 2 grabbed"));
            Assert.That(_handler.Queries, Is.EqualTo(new[] { "UFC 314" }), "same query for both parts runs once, first-with-results stops");
            Assert.That(_handler.Categories, Is.EqualTo(new[] { "5060" }));
            Assert.That(grabs.Select(g => (g.Title, g.EpisodeIds.Single())), Is.EquivalentTo(new[]
            {
                ("UFC.314.1080p.WEB-DL.h264-GRP", mainCardId),
                ("UFC 314 Prelims 1080p WEB h264", prelimsId),
            }));
            Assert.That(searched, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Interactive_search_returns_every_decision_and_grabs_with_override()
    {
        var (prelimsId, mainCardId) = await SeedAsync();

        var releases = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/release?episodeId={mainCardId}"))!.Select(r => r!.AsObject()).ToList();
        var best = releases[0];
        var prelims = releases.Single(r => (string)r["title"]! == "UFC 314 Prelims 1080p WEB h264");
        var sd = releases.Single(r => (string)r["title"]! == "UFC 314 480p HDTV x264");

        var grab = await _client.PostAsJsonAsync("/api/v1/release", new JsonObject { ["guid"] = (string)best["guid"]!, ["indexerId"] = (int)best["indexerId"]! });
        var assigned = await _client.PostAsJsonAsync("/api/v1/release", new JsonObject
        {
            ["guid"] = (string)sd["guid"]!,
            ["indexerId"] = (int)sd["indexerId"]!,
            ["episodeIds"] = new JsonArray(prelimsId, mainCardId),
        });
        var unknown = await _client.PostAsJsonAsync("/api/v1/release", new JsonObject { ["guid"] = "nope", ["indexerId"] = 1 });
        var invalid = await _client.PostAsJsonAsync("/api/v1/release", new JsonObject());
        var missing = await _client.GetAsync("/api/v1/release");
        var languages = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/language"))!;
        var profile = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/qualityprofile"))![0]!;
        var grabs = _grabs.Grabs;

        Assert.Multiple(() =>
        {
            Assert.That(releases, Has.Count.EqualTo(5));
            Assert.That((string)best["title"]!, Is.EqualTo("UFC.314.1080p.WEB-DL.h264-GRP"));
            Assert.That((bool)best["approved"]!, Is.True);
            Assert.That((string)best["quality"]!["quality"]!["name"]!, Is.EqualTo("WEBDL-1080p"));
            Assert.That((int)best["seeders"]!, Is.EqualTo(80));
            Assert.That((int)best["leechers"]!, Is.EqualTo(10));
            Assert.That((string)best["indexer"]!, Is.EqualTo("Tracker"));
            Assert.That((string)best["protocol"]!, Is.EqualTo("torrent"));
            Assert.That((int)best["mappedEpisodeInfo"]![0]!["id"]!, Is.EqualTo(mainCardId));
            Assert.That((string)best["languages"]![0]!["name"]!, Is.EqualTo("Unknown"));
            Assert.That(languages.Take(2).Select(l => (string)l!["name"]!), Is.EqualTo(new[] { "Any", "Original" }));
            Assert.That(languages.Select(l => (int)l!["id"]!), Does.Contain(1).And.Contain(11));
            Assert.That((string)profile["language"]!["name"]!, Is.EqualTo("English"));
            Assert.That(prelims["rejections"]!.AsArray().Select(r => (string)r!), Is.EqualTo(new[] { "Matches S2026E02, which wasn't requested" }));
            Assert.That((string)sd["rejections"]![0]!, Is.EqualTo("SDTV is not wanted in profile"));
            Assert.That(releases.Single(r => ((string)r["title"]!).Contains("Fight Night"))["rejections"]![0]!.GetValue<string>(), Does.StartWith("Does not match S2026E03: Does not match required rule"));
            Assert.That(grab.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(assigned.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(grabs.Select(g => g.EpisodeIds.Count), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        });
    }

    [Test]
    public async Task Rss_sync_grabs_releases_for_wanted_episodes()
    {
        var (_, mainCardId) = await SeedAsync();

        var command = await _client.PostAsJsonAsync("/api/v1/command", new JsonObject { ["name"] = "RssSync" });
        var finished = await WaitAsync((int)(await command.Content.ReadFromJsonAsync<JsonObject>())!["id"]!);
        var grabs = _grabs.Grabs;
        var settings = await _client.GetFromJsonAsync<JsonObject>("/api/v1/settings/indexer");
        var badInterval = await _client.PutAsJsonAsync("/api/v1/settings/indexer", new JsonObject { ["rssSyncInterval"] = 5 });

        Assert.Multiple(() =>
        {
            Assert.That((string)finished["message"]!, Is.EqualTo("5 release(s) evaluated against 2 wanted episode(s), 1 grabbed"));
            Assert.That(_handler.Queries, Is.EqualTo(new[] { "" }));
            Assert.That(grabs.Single().EpisodeIds, Is.EqualTo(new[] { mainCardId }), "prelims is unmonitored");
            Assert.That((int)settings!["rssSyncInterval"]!, Is.EqualTo(15));
            Assert.That(badInterval.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        });
    }

    /// <summary>UFC 314 prelims (unmonitored) and main card, aired two hours ago, plus UFC 315 next month.</summary>
    private async Task<(int Prelims, int MainCard)> SeedAsync()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var profileId = await db.QualityProfiles.Where(p => p.Name == "HD-1080p").Select(p => p.Id).SingleAsync();
        var series = new Series { Title = "UFC", SortTitle = "ufc", CleanTitle = "ufc", TitleSlug = "ufc", Path = "/tv/UFC", RootFolderPath = "/tv", QualityProfileId = profileId };
        series.SearchSettings = new JsonObject { ["categories"] = new JsonArray(5060), ["dateWindow"] = new JsonObject { ["before"] = "6h", ["after"] = "90d" } };
        var air = DateTime.UtcNow.AddHours(-2);
        Episode Part(int number, string title, string extraMust, string extraMustNot, int ufc = 314) => new()
        {
            Series = series,
            SeasonNumber = 2026,
            EpisodeNumber = number,
            Title = title,
            AirDateUtc = ufc == 314 ? air : air.AddDays(30),
            Runtime = 120,
            SearchTerms = new JsonObject
            {
                ["queries"] = new JsonArray($"UFC {ufc}"),
                ["must"] = new JsonArray([$@"\bUFC ?{ufc}\b", .. (extraMust == "" ? Array.Empty<JsonNode>() : [extraMust])]),
                ["mustNot"] = new JsonArray(@"\bfight ?night\b", extraMustNot),
            },
        };
        var prelims = Part(2, "UFC 314 - Prelims", @"\bprelims?\b", @"\bearly ?prelims?\b");
        prelims.Monitored = false;
        var mainCard = Part(3, "UFC 314 - Main Card", "", @"\bprelims?\b");
        db.Episodes.AddRange(prelims, mainCard, Part(4, "UFC 315 - Main Card", "", @"\bprelims?\b", 315));
        db.Indexers.Add(new Indexer
        {
            Name = "Tracker",
            Implementation = IndexerImplementations.Torznab,
            Settings = new JsonObject { ["baseUrl"] = "https://tracker.example", ["apiKey"] = "secret", ["minimumSeeders"] = 1 },
        });
        await db.SaveChangesAsync();
        _handler.PublishDate = air.AddHours(3);
        return (prelims.Id, mainCard.Id);
    }

    private async Task<JsonObject> WaitAsync(int id)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var command = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/command/{id}"))!;
            if ((string)command["status"]! is "completed" or "failed" || DateTime.UtcNow > deadline)
            {
                return command;
            }

            await Task.Delay(50);
        }
    }

    private sealed class TorznabHandler : HttpMessageHandler
    {
        public DateTime PublishDate { get; set; }

        public List<string> Queries { get; } = [];

        public List<string> Categories { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            lock (Queries)
            {
                Queries.Add(query["q"] ?? "");
                Categories.Add(query["cat"] ?? "");
            }

            var items = string.Concat(new[]
            {
                Item("UFC.314.1080p.WEB-DL.h264-GRP", 6, 80),
                Item("UFC 314 1080p WEB h264 lowseed", 6, 2),
                Item("UFC 314 Prelims 1080p WEB h264", 4, 40),
                Item("UFC 314 480p HDTV x264", 1, 100),
                Item("UFC Fight Night 314 1080p WEB h264", 6, 100),
            });
            var xml = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed"><channel>{items}</channel></rss>
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") });
        }

        private string Item(string title, int gigabytes, int seeders) => $"""
            <item><title>{title}</title><guid>{Uri.EscapeDataString(title)}</guid><pubDate>{PublishDate:R}</pubDate>
            <size>{(long)gigabytes << 30}</size><link>https://tracker.example/dl/{Uri.EscapeDataString(title)}</link>
            <torznab:attr name="seeders" value="{seeders}" /><torznab:attr name="peers" value="{seeders + 10}" /></item>
            """;
    }

    /// <summary>Records grabs, most recent first.</summary>
    private sealed class RecordingDownloadService : IDownloadService
    {
        public List<(string Title, IReadOnlyList<int> EpisodeIds)> Grabs { get; } = [];

        public Task<string> GrabAsync(RemoteEpisode remoteEpisode, CancellationToken cancellationToken = default)
        {
            lock (Grabs)
            {
                Grabs.Insert(0, (remoteEpisode.Release.Title, remoteEpisode.Episodes.Select(e => e.Id).ToList()));
            }

            return Task.FromResult(remoteEpisode.Release.Guid);
        }
    }
}
