using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Download;
using Generral.Core.Download.Clients.QBittorrent;
using Generral.Core.Download.TrackedDownloads;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.Messaging;
using Generral.Core.Notifications;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Generral.Integration.Test;

/// <summary>Grab → qBittorrent → history → queue → failed → blocklist → search again, against fake tracker and qBittorrent.</summary>
public class DownloadApiTests
{
    private const string Hash = "0123456789ABCDEF0123456789ABCDEF01234567";

    private FakeQBittorrent _qbit = null!;
    private FakeTracker _tracker = null!;
    private FakeWebhook _webhook = null!;
    private GenerralTestHost _host = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        (_qbit, _tracker, _webhook) = (new FakeQBittorrent(), new FakeTracker(), new FakeWebhook());
        _host = await GenerralTestHost.StartAsync(services: services =>
        {
            services.PostConfigure<HttpClientFactoryOptions>(QBittorrentClient.ClientName, o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = _qbit));
            services.PostConfigure<HttpClientFactoryOptions>(TorznabClient.ClientName, o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = _tracker));
            services.PostConfigure<HttpClientFactoryOptions>(NotificationService.ClientName, o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = _webhook));
        });
        _client = _host.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", _host.ApiKey);
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        _qbit.Dispose();
        _tracker.Dispose();
        _webhook.Dispose();
    }

    [Test]
    public async Task Download_client_crud_schema_and_test()
    {
        var schema = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/downloadclient/schema"))!.Single()!.AsObject();
        var fields = schema["fields"]!.AsArray().ToDictionary(f => (string)f!["name"]!, f => f!["value"]);
        var created = await CreateClientAsync();
        var stored = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/downloadclient/{(int)created["id"]!}"))!;
        var badLogin = await _client.PostAsJsonAsync("/api/v1/downloadclient/test", ClientResource("wrong"));
        var invalid = await _client.PostAsJsonAsync("/api/v1/downloadclient", ClientResource("secret", port: 0));

        Assert.Multiple(() =>
        {
            Assert.That((string)schema["implementation"]!, Is.EqualTo("QBittorrent"));
            Assert.That((string)fields["host"]!, Is.EqualTo("localhost"));
            Assert.That((int)fields["port"]!, Is.EqualTo(8080));
            Assert.That((string)fields["tvCategory"]!, Is.EqualTo("generral"));
            Assert.That((string)stored["fields"]!.AsArray().Single(f => (string)f!["name"]! == "password")!["value"]!, Is.EqualTo("********"));
            Assert.That((int)stored["priority"]!, Is.EqualTo(1));
            Assert.That(badLogin.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        });
    }

    [Test]
    public async Task Grab_records_history_and_failed_download_blocklists_and_searches_again()
    {
        await CreateClientAsync();
        var episodeId = await SeedAsync();

        var search = await RunAsync("EpisodeSearch", new JsonObject { ["episodeIds"] = new JsonArray(episodeId) });
        var history = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/history?episodeId={episodeId}"))!;
        var add = _qbit.Requests.Single(r => r.Path == "/api/v2/torrents/add");

        _qbit.State = "downloading";
        await RunAsync(RefreshMonitoredDownloadsHandler.CommandName);
        var downloading = (await _client.GetFromJsonAsync<JsonObject>("/api/v1/queue"))!;

        _qbit.State = "stoppedUP";
        await RunAsync(RefreshMonitoredDownloadsHandler.CommandName);
        var completed = (await _client.GetFromJsonAsync<JsonObject>("/api/v1/queue"))!["records"]![0]!;

        var queueId = (int)completed["id"]!;
        var remove = await _client.DeleteAsync($"/api/v1/queue/{queueId}?removeFromClient=true&blocklist=true");
        var blocklist = (await _client.GetFromJsonAsync<JsonObject>("/api/v1/blocklist"))!;
        var failedHistory = (await _client.GetFromJsonAsync<JsonObject>("/api/v1/history?eventType=downloadFailed"))!;
        var commands = _host.Services.GetRequiredService<CommandQueue>().All();
        var releases = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/release?episodeId={episodeId}"))!;

        Assert.Multiple(() =>
        {
            Assert.That((string)search["message"]!, Does.EndWith("1 grabbed"), (string?)search["message"]);
            Assert.That(_tracker.Downloads, Is.EqualTo(1), "the .torrent is fetched by Generral");
            Assert.That(add.Body, Does.Contain("name=torrents"));
            Assert.That((int)history["totalRecords"]!, Is.EqualTo(1));
            var grabbed = history["records"]![0]!;
            Assert.That((string)grabbed["eventType"]!, Is.EqualTo("grabbed"));
            Assert.That((string)grabbed["downloadId"]!, Is.EqualTo(_tracker.TorrentHash));
            Assert.That((string)grabbed["quality"]!["quality"]!["name"]!, Is.EqualTo("WEBDL-1080p"));
            Assert.That((string)grabbed["data"]!["indexer"]!, Is.EqualTo("Tracker"));
            Assert.That((string)grabbed["data"]!["downloadClientName"]!, Is.EqualTo("qBittorrent"));

            var item = downloading["records"]![0]!;
            Assert.That((int)downloading["totalRecords"]!, Is.EqualTo(1));
            Assert.That((string)item["status"]!, Is.EqualTo("downloading"));
            Assert.That((string)item["trackedDownloadState"]!, Is.EqualTo("downloading"));
            Assert.That((int)item["episodeId"]!, Is.EqualTo(episodeId));
            Assert.That((string)item["downloadClient"]!, Is.EqualTo("qBittorrent"));
            Assert.That((string)completed["trackedDownloadState"]!, Is.EqualTo("importBlocked"));
            Assert.That((string)completed["statusMessages"]![0]!["messages"]![0]!, Does.Contain("reports files in /downloads/UFC 314 but this path doesn't exist"));

            Assert.That(remove.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(_qbit.Requests.Single(r => r.Path == "/api/v2/torrents/delete").Body, Does.Contain("deleteFiles=true"));
            Assert.That((int)blocklist["totalRecords"]!, Is.EqualTo(1));
            Assert.That((string)blocklist["records"]![0]!["message"]!, Is.EqualTo("Manually marked as failed"));
            Assert.That((int)failedHistory["totalRecords"]!, Is.EqualTo(1));
            Assert.That(commands.Count(c => c.Name == "EpisodeSearch"), Is.EqualTo(2), "searched again");
            Assert.That(releases.Select(r => r!["rejections"]!.AsArray().Select(x => (string)x!)), Has.Some.Contains("Release is blocklisted"));
        });
    }

    [Test]
    public async Task Client_error_is_handled_as_failed()
    {
        await CreateClientAsync();
        var episodeId = await SeedAsync();
        await RunAsync("EpisodeSearch", new JsonObject { ["episodeIds"] = new JsonArray(episodeId) });

        var tracked = _host.Services.GetRequiredService<TrackedDownloadService>();
        var queued = tracked.QueuedEpisodes();
        _qbit.State = "error";
        await RunAsync(RefreshMonitoredDownloadsHandler.CommandName);
        var warning = tracked.Queue().Single();

        Assert.Multiple(() =>
        {
            Assert.That(queued.Keys, Is.EqualTo(new[] { episodeId }), "already in queue");
            Assert.That(warning.Status, Is.EqualTo(TrackedDownloadStatus.Warning), "qBittorrent 'error' is a warning, not a failure (Sonarr parity)");
            Assert.That(warning.State, Is.EqualTo(TrackedDownloadState.Downloading));
        });
    }

    [Test]
    public async Task Completed_download_is_imported_through_a_remote_path_mapping_and_an_upgrade_recycles_the_old_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "generral-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            var downloads = Directory.CreateDirectory(Path.Combine(root, "downloads", "UFC 314")).FullName;
            var library = Directory.CreateDirectory(Path.Combine(root, "tv")).FullName;
            var bin = Path.Combine(root, "bin");
            await File.WriteAllBytesAsync(Path.Combine(downloads, "ufc.314.main.card.1080p.web-dl-GRP.mkv"), new byte[2000]);
            await File.WriteAllBytesAsync(Path.Combine(downloads, "ufc.314.main.card.1080p.web-dl-GRP.en.srt"), new byte[10]);
            await File.WriteAllBytesAsync(Path.Combine(downloads, "sample.mkv"), new byte[2000]);
            _qbit.SavePath = "/remote/";

            await CreateClientAsync();
            var episodeId = await SeedAsync(Path.Combine(library, "UFC"));
            var rootFolder = await _client.PostAsJsonAsync("/api/v1/rootfolder", new JsonObject { ["path"] = library });
            var mapping = await _client.PostAsJsonAsync("/api/v1/remotepathmapping", new JsonObject { ["host"] = "localhost", ["remotePath"] = "/remote", ["localPath"] = Path.Combine(root, "downloads") });
            var media = (await _client.GetFromJsonAsync<JsonObject>("/api/v1/config/mediamanagement"))!;
            media["recycleBin"] = bin;
            Assert.That((await _client.PutAsJsonAsync("/api/v1/config/mediamanagement", media)).StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            await AddConnectionsAsync();

            await RunAsync("EpisodeSearch", new JsonObject { ["episodeIds"] = new JsonArray(episodeId) });
            _qbit.State = "stoppedUP";
            var refresh = await RunAsync(RefreshMonitoredDownloadsHandler.CommandName);

            var queue = (await _client.GetFromJsonAsync<JsonObject>("/api/v1/queue"))!;
            var episode = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/episode/{episodeId}"))!;
            var files = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/episodefile?seriesId={(int)episode["seriesId"]!}"))!;
            var imported = (await _client.GetFromJsonAsync<JsonObject>("/api/v1/history?eventType=downloadFolderImported"))!;
            var series = (await _client.GetFromJsonAsync<JsonObject>($"/api/v1/series/{(int)episode["seriesId"]!}"))!;
            var roots = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/rootfolder"))!;
            var expected = Path.Combine(library, "UFC", "Season 2026", "UFC - S2026E03 - UFC 314 - Main Card [WEBDL-1080p].mkv");
            var source = Path.Combine(downloads, "ufc.314.main.card.1080p.web-dl-GRP.mkv");

            Assert.Multiple(() =>
            {
                Assert.That(rootFolder.StatusCode, Is.EqualTo(HttpStatusCode.Created));
                Assert.That(mapping.StatusCode, Is.EqualTo(HttpStatusCode.Created));
                Assert.That((string)refresh["message"]!, Does.Contain("1 imported"), (string?)refresh["message"]);
                Assert.That((int)queue["totalRecords"]!, Is.EqualTo(0), "imported downloads leave the queue");
                Assert.That(File.Exists(expected), expected);
                Assert.That(File.Exists(Path.ChangeExtension(expected, ".en.srt")), "subtitle carried over");
                Assert.That(File.Exists(source), "the torrent keeps seeding");
                Assert.That(IsHardLink(source, expected), "hardlinked");
                Assert.That((bool)episode["hasFile"]!, Is.True);
                Assert.That((int)episode["episodeFileId"]!, Is.EqualTo((int)files.Single()!["id"]!));
                Assert.That((string)files[0]!["relativePath"]!, Is.EqualTo(Path.Combine("Season 2026", "UFC - S2026E03 - UFC 314 - Main Card [WEBDL-1080p].mkv")));
                Assert.That((string)files[0]!["releaseGroup"]!, Is.EqualTo("GRP"));
                Assert.That((string)files[0]!["sceneName"]!, Is.EqualTo("UFC.314.1080p.WEB-DL.h264-GRP"));
                Assert.That((int)imported["totalRecords"]!, Is.EqualTo(1));
                Assert.That((long)series["statistics"]!["sizeOnDisk"]!, Is.EqualTo(2000));
                Assert.That((int)series["statistics"]!["episodeFileCount"]!, Is.EqualTo(1));
                Assert.That((bool)roots.Single()!["accessible"]!, Is.True);
                Assert.That(roots[0]!["unmappedFolders"]!.AsArray(), Is.Empty, "the series folder is mapped");
                Assert.That(File.ReadAllText(Path.ChangeExtension(expected, ".nfo")), Does.Contain("<title>UFC 314 - Main Card</title>").And.Contain($"<uniqueid type=\"generral\" default=\"true\">ep-{episodeId}</uniqueid>"));
                Assert.That(File.ReadAllText(Path.Combine(library, "UFC", "tvshow.nfo")), Does.Contain("<title>UFC</title>"));
                Assert.That(_webhook.EventTypes, Is.EqualTo(new[] { "Test", "Grab", "Download" }));
            });

            // The HD-1080p profile's cutoff is HDTV-1080p and WEBDL-1080p is above it, so search rejects; a manual Bluray import upgrades.
            var releases = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/release?episodeId={episodeId}"))!;
            var better = Path.Combine(root, "manual", "UFC.314.Main.Card.1080p.BluRay.x264-HD.mkv");
            Directory.CreateDirectory(Path.GetDirectoryName(better)!);
            await File.WriteAllBytesAsync(better, new byte[3000]);
            var listed = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/manualimport?folder={Uri.EscapeDataString(Path.GetDirectoryName(better)!)}&seriesId={(int)episode["seriesId"]!}"))!;
            var manual = await RunAsync("ManualImport", new JsonObject
            {
                ["importMode"] = "move",
                ["files"] = new JsonArray(new JsonObject { ["path"] = better, ["seriesId"] = (int)episode["seriesId"]!, ["episodeIds"] = new JsonArray(episodeId) }),
            });
            var worse = await RunAsync("ManualImport", new JsonObject
            {
                ["files"] = new JsonArray(new JsonObject { ["path"] = source, ["seriesId"] = (int)episode["seriesId"]!, ["episodeIds"] = new JsonArray(episodeId) }),
            });
            var after = (await _client.GetFromJsonAsync<JsonArray>($"/api/v1/episodefile?seriesId={(int)episode["seriesId"]!}"))!;

            Assert.Multiple(() =>
            {
                Assert.That(releases.Single()!["rejections"]!.AsArray().Select(r => (string)r!), Has.Some.StartsWith("Existing file meets cutoff"));
                Assert.That((string)listed.Single()!["quality"]!["quality"]!["name"]!, Is.EqualTo("Bluray-1080p"));
                Assert.That((string)manual["status"]!, Is.EqualTo("completed"), (string?)manual["message"]);
                Assert.That((string)after.Single()!["quality"]!["quality"]!["name"]!, Is.EqualTo("Bluray-1080p"));
                Assert.That(File.Exists(better), Is.False, "moved");
                Assert.That(File.Exists(expected), Is.False);
                Assert.That(File.Exists(Path.Combine(bin, "UFC", "Season 2026", Path.GetFileName(expected))), "old file recycled");
                Assert.That((string)worse["status"]!, Is.EqualTo("failed"));
                Assert.That((string)worse["message"]!, Does.Contain("Not an upgrade for existing episode file(s)"));
                Assert.That(_webhook.EventTypes.Skip(3), Is.EqualTo(new[] { "EpisodeFileDelete", "Download" }), "upgrade: old file deleted, then imported");
                Assert.That(File.Exists(Path.ChangeExtension(expected, ".nfo")), Is.False, "the old file's NFO is removed with it");
                Assert.That(Directory.GetFiles(Path.Combine(library, "UFC", "Season 2026"), "*.nfo").Single(), Does.Contain("Bluray-1080p"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Failed_import_shows_the_reason_in_the_queue()
    {
        var root = Path.Combine(Path.GetTempPath(), "generral-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            var downloads = Directory.CreateDirectory(Path.Combine(root, "UFC 314")).FullName;
            await File.WriteAllBytesAsync(Path.Combine(downloads, "readme.txt"), new byte[10]);
            _qbit.SavePath = root + "/";
            await CreateClientAsync();
            var episodeId = await SeedAsync(Path.Combine(root, "tv", "UFC"));
            await RunAsync("EpisodeSearch", new JsonObject { ["episodeIds"] = new JsonArray(episodeId) });
            _qbit.State = "stoppedUP";
            await RunAsync(RefreshMonitoredDownloadsHandler.CommandName);
            var item = (await _client.GetFromJsonAsync<JsonObject>("/api/v1/queue"))!["records"]![0]!;

            Assert.Multiple(() =>
            {
                Assert.That((string)item["trackedDownloadState"]!, Is.EqualTo("importBlocked"));
                Assert.That((string)item["trackedDownloadStatus"]!, Is.EqualTo("warning"));
                Assert.That((string)item["statusMessages"]![0]!["messages"]![0]!, Does.StartWith("No files found are eligible for import"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Writing through one path changes the other only when both are the same inode.</summary>
    private static bool IsHardLink(string a, string b)
    {
        using (var stream = new FileStream(a, FileMode.Open, FileAccess.Write))
        {
            stream.WriteByte(42);
        }

        return File.ReadAllBytes(b)[0] == 42;
    }

    /// <summary>Plex NFO metadata and a webhook for every trigger, through the API.</summary>
    private async Task AddConnectionsAsync()
    {
        var metadataSchema = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/metadata/schema"))!.Single()!.AsObject();
        metadataSchema["name"] = "Plex NFO";
        metadataSchema["enable"] = true;
        var metadata = await _client.PostAsJsonAsync("/api/v1/metadata", metadataSchema);
        var schema = (await _client.GetFromJsonAsync<JsonArray>("/api/v1/notification/schema"))!;
        var webhook = schema.Single(s => (string)s!["implementation"]! == "Webhook")!.AsObject();
        webhook["name"] = "Hook";
        webhook["fields"]!.AsArray().Single(f => (string)f!["name"]! == "url")!["value"] = "https://hook.example/generral";
        foreach (var trigger in new[] { "onGrab", "onDownload", "onUpgrade", "onEpisodeFileDelete", "onEpisodeFileDeleteForUpgrade" })
        {
            webhook[trigger] = true;
        }

        var created = await _client.PostAsJsonAsync("/api/v1/notification", webhook);
        var plex = schema.Single(s => (string)s!["implementation"]! == "PlexServer")!;
        var (metadataBody, createdBody) = (await metadata.Content.ReadAsStringAsync(), await created.Content.ReadAsStringAsync());
        Assert.Multiple(() =>
        {
            Assert.That(metadata.StatusCode, Is.EqualTo(HttpStatusCode.Created), metadataBody);
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), createdBody);
            Assert.That((bool)plex["supportsOnGrab"]!, Is.False);
            Assert.That((bool)plex["supportsOnDownload"]!, Is.True);
            Assert.That(schema.Select(s => (string)s!["implementation"]!), Is.EquivalentTo(new[] { "Discord", "PlexServer", "Webhook" }));
        });
    }

    private async Task<JsonObject> CreateClientAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/downloadclient", ClientResource("secret"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private static JsonObject ClientResource(string password, int port = 8111) => new()
    {
        ["name"] = "qBittorrent",
        ["implementation"] = "QBittorrent",
        ["enable"] = true,
        ["fields"] = new JsonArray(
            new JsonObject { ["name"] = "host", ["value"] = "localhost" },
            new JsonObject { ["name"] = "port", ["value"] = port },
            new JsonObject { ["name"] = "username", ["value"] = "admin" },
            new JsonObject { ["name"] = "password", ["value"] = password }),
    };

    private async Task<int> SeedAsync(string seriesPath = "/tv/UFC")
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var profileId = await db.QualityProfiles.Where(p => p.Name == "HD-1080p").Select(p => p.Id).SingleAsync();
        var series = new Series { Title = "UFC", SortTitle = "ufc", CleanTitle = "ufc", TitleSlug = "ufc", Path = seriesPath, RootFolderPath = Path.GetDirectoryName(seriesPath)!, QualityProfileId = profileId };
        var episode = new Episode
        {
            Series = series,
            SeasonNumber = 2026,
            EpisodeNumber = 3,
            Title = "UFC 314 - Main Card",
            AirDateUtc = DateTime.UtcNow.AddHours(-2),
            Runtime = 120,
            SearchTerms = new JsonObject { ["queries"] = new JsonArray("UFC 314"), ["must"] = new JsonArray(@"\bUFC ?314\b") },
        };
        db.Episodes.Add(episode);
        db.Seasons.Add(new Season { Series = series, SeasonNumber = 2026 });
        db.Indexers.Add(new Indexer
        {
            Name = "Tracker",
            Implementation = IndexerImplementations.Torznab,
            Settings = new JsonObject { ["baseUrl"] = "https://tracker.example", ["apiKey"] = "secret" },
        });
        await db.SaveChangesAsync();
        return episode.Id;
    }

    private async Task<JsonObject> RunAsync(string name, JsonObject? body = null)
    {
        body ??= [];
        body["name"] = name;
        var response = await _client.PostAsJsonAsync("/api/v1/command", body);
        var id = (int)(await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!;
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

    private sealed record Request(string Path, string Body);

    private sealed class FakeWebhook : HttpMessageHandler
    {
        public List<string> EventTypes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            lock (EventTypes)
            {
                EventTypes.Add((string)body["eventType"]!);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FakeTracker : HttpMessageHandler
    {
        private static readonly byte[] Torrent = Encoding.ASCII.GetBytes("d4:infod6:lengthi5e4:name7:UFC 314ee");

        public int Downloads { get; private set; }

        public string TorrentHash => TorrentInfoHash.FromTorrentFile(Torrent);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.StartsWith("/dl/", StringComparison.Ordinal))
            {
                Downloads++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Torrent) });
            }

            var xml = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed"><channel>
                <item><title>UFC.314.1080p.WEB-DL.h264-GRP</title><guid>ufc314</guid><pubDate>{DateTime.UtcNow.AddHours(-1):R}</pubDate>
                <size>{6L << 30}</size><link>https://tracker.example/dl/ufc314</link>
                <torznab:attr name="seeders" value="80" /></item>
                </channel></rss>
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") });
        }
    }

    private sealed class FakeQBittorrent : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];

        public string State { get; set; } = "queuedDL";

        public string? Hash { get; private set; }

        public string SavePath { get; set; } = "/downloads/";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            lock (Requests)
            {
                Requests.Add(new Request(path, body));
            }

            if (path == "/api/v2/auth/login")
            {
                var ok = Text(body.Contains("password=secret", StringComparison.Ordinal) ? "Ok." : "Fails.");
                ok.Headers.Add("Set-Cookie", "SID=abc; path=/");
                return ok;
            }

            if (path == "/api/v2/torrents/add")
            {
                Hash = "added";
            }

            if (path == "/api/v2/torrents/delete")
            {
                Hash = null;
            }

            var torrents = Hash is null
                ? "[]"
                : $$"""[{"hash":"{{new FakeTracker().TorrentHash.ToLowerInvariant()}}","name":"UFC 314","size":1000,"progress":{{(State == "stoppedUP" ? 1 : 0.5)}},"eta":60,"state":"{{State}}","category":"generral","save_path":"{{SavePath}}","content_path":"{{SavePath}}UFC 314","ratio":0}]""";
            return path switch
            {
                "/api/v2/app/webapiVersion" => Text("2.11.2"),
                "/api/v2/app/preferences" => Text("""{"dht":true}"""),
                "/api/v2/torrents/categories" => Text("""{"generral":{}}"""),
                "/api/v2/torrents/info" => Text(torrents),
                _ => Text(""),
            };
        }

        private static HttpResponseMessage Text(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    }
}
