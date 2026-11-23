using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Generral.Core.Configuration;
using Generral.Core.DecisionEngine;
using Generral.Core.Extras.Metadata;
using Generral.Core.Extras.Metadata.Consumers.PlexNfo;
using Generral.Core.Indexers;
using Generral.Core.MediaFiles;
using Generral.Core.Notifications;
using Generral.Core.Notifications.Discord;
using Generral.Core.Notifications.Plex;
using Generral.Core.Notifications.Webhook;
using Generral.Core.Qualities;
using Generral.Core.Tv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Generral.Core.Test.Notifications;

public class MetadataAndNotificationTests
{
    private static readonly DateTime Now = new(2025, 4, 20, 12, 0, 0, DateTimeKind.Utc);

    private FakeHandler _handler = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new FakeHandler();
        var services = new ServiceCollection();
        services.AddHttpClient(NotificationService.ClientName).ConfigurePrimaryHttpMessageHandler(() => _handler);
        _services = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _handler.Dispose();
    }

    [Test]
    public void Plex_nfo_for_a_UC1_episode_and_series()
    {
        var nfo = new PlexNfoMetadata();
        var series = Series();
        var episode = Episode();

        var episodeNfo = nfo.EpisodeMetadata(File(), [episode], [])!;
        var seriesNfo = nfo.SeriesMetadata(new MetadataSeries(series, [new Season { SeasonNumber = 2025, Title = "2025" }], new DateOnly(1993, 11, 12)), [])!;

        Assert.Multiple(() =>
        {
            Assert.That(episodeNfo.RelativePath, Is.EqualTo(Path.Combine("Season 2025", "UFC - S2025E04 - UFC 314 - Main Card [WEBDL-1080p].nfo")));
            Assert.That(episodeNfo.Contents.ReplaceLineEndings("\n"), Is.EqualTo("""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <episodedetails>
                  <title>UFC 314: Volkanovski vs. Lopes - Main Card</title>
                  <season>2025</season>
                  <episode>4</episode>
                  <plot>Kaseya Center, Miami.</plot>
                  <aired>2025-04-12</aired>
                  <runtime>180</runtime>
                  <uniqueid type="generral" default="true">ep-1234</uniqueid>
                </episodedetails>

                """));
            Assert.That(seriesNfo.RelativePath, Is.EqualTo("tvshow.nfo"));
            Assert.That(seriesNfo.Contents, Does.Contain("<title>UFC</title>").And.Contain("<premiered>1993-11-12</premiered>").And.Contain("<genre>MMA</genre>")
                .And.Contain("<uniqueid type=\"generral\" default=\"true\">series-8f1c</uniqueid>").And.Contain("<namedseason number=\"2025\">2025</namedseason>"));
            Assert.That(nfo.SeriesImages(series, []).Select(i => i.RelativePath), Is.EqualTo(new[] { "poster.jpg", "fanart.jpg" }));
            Assert.That(nfo.SeasonImages(series, new Season { SeasonNumber = 2025, Images = new() { Poster = "https://img/s.jpg" } }, "Season 2025", []).Single().RelativePath,
                Is.EqualTo(Path.Combine("Season 2025", "Season2025.jpg")));
            Assert.That(nfo.EpisodeMetadata(File(), [episode], new JsonObject { ["episodeMetadata"] = false }), Is.Null, "artwork only (approach B)");
        });
    }

    [Test]
    public void Spoiler_free_mode_drops_overview_and_thumb_once_aired_but_keeps_the_poster()
    {
        var aired = SpoilerFilter.Apply(Episode(), spoilerFree: true, Now);
        var upcoming = SpoilerFilter.Apply(Episode(), spoilerFree: true, new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var off = SpoilerFilter.Apply(Episode(), spoilerFree: false, Now);
        var nfo = new PlexNfoMetadata();

        Assert.Multiple(() =>
        {
            Assert.That(aired.Overview, Is.Null);
            Assert.That(aired.Images.Thumb, Is.Null);
            Assert.That(aired.Images.Poster, Is.EqualTo("https://img/poster.jpg"));
            Assert.That(aired.Title, Is.EqualTo("UFC 314: Volkanovski vs. Lopes - Main Card"));
            Assert.That(nfo.EpisodeMetadata(File(), [aired], [])!.Contents, Does.Not.Contain("<plot>"));
            Assert.That(nfo.EpisodeImages(File(), [aired], []).Single().Url, Is.EqualTo("https://img/poster.jpg"), "promo poster replaces the redacted thumb");
            Assert.That(nfo.EpisodeImages(File(), [new Episode { Title = "No art" }], []), Is.Empty);
            Assert.That(nfo.EpisodeImages(File(), [Episode()], []).Single().Url, Is.EqualTo("https://img/thumb.jpg"), "thumb wins over poster");
            Assert.That(upcoming.Overview, Is.Not.Null);
            Assert.That(off.Overview, Is.Not.Null);
        });
    }

    [Test]
    public async Task Plex_refreshes_the_series_folder_in_the_section_whose_location_contains_it()
    {
        _handler.Respond = request => request.RequestUri!.AbsolutePath == "/library/sections"
            ? """{"MediaContainer":{"Directory":[{"key":"1","type":"movie","Location":[{"id":1,"path":"/media/movies"}]},{"key":"2","type":"show","agent":"tv.plex.agents.none","Location":[{"id":2,"path":"/media/tv"}]},{"key":"3","type":"show","Location":[{"id":3,"path":"/media/generral"}]}]}}"""
            : "";
        var plex = new PlexServerNotification(_services.GetRequiredService<IHttpClientFactory>(), NullLogger<PlexServerNotification>.Instance);
        var definition = new NotificationDefinition
        {
            Name = "Plex",
            Implementation = PlexServerNotification.Name,
            Settings = new JsonObject { ["host"] = "plex.lan", ["port"] = 32400, ["authToken"] = "tok", ["mapFrom"] = "/data/media/generral", ["mapTo"] = "/media/generral" },
        };

        var series = Series();
        series.Path = "/data/media/generral/UFC";
        await plex.SendAsync(definition, new DownloadMessage { Series = series, File = File(), SourcePath = "/downloads/x.mkv" });

        var refresh = _handler.Requests.Single(r => r.Uri.AbsolutePath.EndsWith("/refresh", StringComparison.Ordinal));
        Assert.Multiple(() =>
        {
            Assert.That(_handler.Requests[0].Uri.ToString(), Does.StartWith("http://plex.lan:32400/library/sections?"));
            Assert.That(_handler.Requests.Select(r => r.Token), Is.All.EqualTo("tok"), "token sent as a header, never in the URL");
            Assert.That(refresh.Uri.AbsolutePath, Is.EqualTo("/library/sections/3/refresh"));
            Assert.That(refresh.Uri.Query, Does.Contain("path=%2Fmedia%2Fgenerral%2FUFC"));
        });
    }

    [Test]
    public async Task Plex_skips_the_refresh_when_no_library_covers_the_series()
    {
        _handler.Respond = request => request.RequestUri!.AbsolutePath == "/library/sections"
            ? """{"MediaContainer":{"Directory":[{"key":"2","type":"show","Location":[{"id":2,"path":"/media/tv"}]}]}}"""
            : "";
        var plex = new PlexServerNotification(_services.GetRequiredService<IHttpClientFactory>(), NullLogger<PlexServerNotification>.Instance);
        var definition = new NotificationDefinition
        {
            Name = "Plex",
            Implementation = PlexServerNotification.Name,
            Settings = new JsonObject { ["host"] = "plex.lan", ["port"] = 32400, ["authToken"] = "tok" },
        };

        var series = Series();
        series.Path = "/media/generral/Rugby World Cup";
        await plex.SendAsync(definition, new DownloadMessage { Series = series, File = File(), SourcePath = "/downloads/x.mkv" });

        Assert.That(_handler.Requests.Select(r => r.Uri.AbsolutePath), Is.EqualTo(new[] { "/library/sections" }));
    }

    [Test]
    public async Task Webhook_sends_a_sonarr_shaped_download_payload()
    {
        var webhook = new WebhookNotification(_services.GetRequiredService<IHttpClientFactory>(), _services.GetRequiredService<IServiceScopeFactory>(), new FixedHostConfig());
        var definition = new NotificationDefinition { Name = "Hook", Implementation = WebhookNotification.Name, Settings = new JsonObject { ["url"] = "https://hook.example/x" } };

        await webhook.SendAsync(definition, new DownloadMessage
        {
            Series = Series(),
            Episodes = [Episode()],
            File = File(),
            OldFiles = [File()],
            SourcePath = "/downloads/ufc.314.mkv",
            DownloadClientName = "qBittorrent",
            DownloadClientType = "QBittorrent",
            DownloadId = "ABC",
            Release = new GrabbedRelease("UFC.314.1080p.WEB-DL-GRP", "Tracker", 6000),
        });

        var body = JsonNode.Parse(_handler.Requests.Single().Body)!;
        Assert.Multiple(() =>
        {
            Assert.That((string)body["eventType"]!, Is.EqualTo("Download"));
            Assert.That((string)body["instanceName"]!, Is.EqualTo("Generral"));
            Assert.That((bool)body["isUpgrade"]!, Is.True);
            Assert.That((string)body["series"]!["title"]!, Is.EqualTo("UFC"));
            Assert.That((string)body["series"]!["type"]!, Is.EqualTo("standard"));
            Assert.That((int)body["series"]!["tvdbId"]!, Is.EqualTo(0));
            Assert.That((int)body["episodes"]![0]!["seasonNumber"]!, Is.EqualTo(2025));
            Assert.That((string)body["episodes"]![0]!["airDate"]!, Is.EqualTo("2025-04-12"));
            Assert.That((string)body["episodeFile"]!["quality"]!, Is.EqualTo("WEBDL-1080p"));
            Assert.That((string)body["episodeFile"]!["sourcePath"]!, Is.EqualTo("/downloads/ufc.314.mkv"));
            Assert.That((string)body["release"]!["releaseTitle"]!, Is.EqualTo("UFC.314.1080p.WEB-DL-GRP"));
            Assert.That((string)body["downloadClient"]!, Is.EqualTo("qBittorrent"));
            Assert.That(body["deletedFiles"]!.AsArray(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Discord_grab_embed()
    {
        var discord = new DiscordNotification(_services.GetRequiredService<IHttpClientFactory>(), new FixedHostConfig(), new FixedTime());
        var definition = new NotificationDefinition { Name = "Discord", Implementation = DiscordNotification.Name, Settings = new JsonObject { ["webHookUrl"] = "https://discord.example/api/webhooks/1/x" } };
        var spoilerFree = SpoilerFilter.Apply(Episode(), true, Now);
        var remote = new RemoteEpisode
        {
            Release = new ReleaseInfo { Guid = "g", Title = "UFC.314.1080p.WEB-DL-GRP", Size = 6L << 30, Indexer = "Tracker" },
            Quality = new QualityModel { Quality = Quality.WEBDL1080p },
        };

        var payload = discord.Build(definition, new GrabMessage { Series = Series(), Episodes = [spoilerFree], Remote = remote, DownloadClientName = "qBittorrent", DownloadClientType = "QBittorrent", DownloadId = "ABC" });
        var json = JsonNode.Parse(JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        var embed = json["embeds"]![0]!;

        Assert.Multiple(() =>
        {
            Assert.That((string)embed["title"]!, Is.EqualTo("UFC - S2025E04 - UFC 314: Volkanovski vs. Lopes - Main Card"));
            Assert.That((string)embed["description"]!, Is.EqualTo("Episode Grabbed"));
            Assert.That((int)embed["color"]!, Is.EqualTo((int)DiscordColors.Standard));
            Assert.That((string)embed["author"]!["name"]!, Is.EqualTo("Generral"));
            Assert.That((string)embed["thumbnail"]!["url"]!, Is.EqualTo("https://img/poster.jpg"));
            Assert.That(embed["fields"]!.AsArray().Select(f => (string)f!["name"]!), Is.EqualTo(new[] { "Quality", "Size", "Release", "Indexer", "Genres" }), "no overview once aired");
            Assert.That((string)embed["fields"]![1]!["value"]!, Is.EqualTo("6 GB"));
        });
    }

    private static Series Series() => new()
    {
        Id = 8,
        Title = "UFC",
        SortTitle = "ufc",
        CleanTitle = "ufc",
        TitleSlug = "ufc",
        Path = "/tv/UFC",
        RootFolderPath = "/tv",
        Genres = ["Sport", "MMA"],
        Network = "UFC",
        Guid = "generral://series/8f1c",
        Images = new() { Poster = "https://img/poster.jpg", Fanart = "https://img/fanart.jpg" },
    };

    private static Episode Episode() => new()
    {
        Id = 1234,
        SeriesId = 8,
        SeasonNumber = 2025,
        EpisodeNumber = 4,
        Title = "UFC 314: Volkanovski vs. Lopes - Main Card",
        Overview = "Kaseya Center, Miami.",
        AirDate = new DateOnly(2025, 4, 12),
        AirDateUtc = new DateTime(2025, 4, 13, 2, 0, 0, DateTimeKind.Utc),
        Runtime = 180,
        Images = new() { Thumb = "https://img/thumb.jpg", Poster = "https://img/poster.jpg" },
    };

    private static EpisodeFile File() => new()
    {
        Id = 5,
        SeriesId = 8,
        SeasonNumber = 2025,
        RelativePath = Path.Combine("Season 2025", "UFC - S2025E04 - UFC 314 - Main Card [WEBDL-1080p].mkv"),
        Path = "/tv/UFC/Season 2025/UFC - S2025E04 - UFC 314 - Main Card [WEBDL-1080p].mkv",
        Size = 2000,
        Quality = new QualityModel { Quality = Quality.WEBDL1080p },
    };

    private sealed record Request(Uri Uri, string? Token, string Body);

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];

        public Func<HttpRequestMessage, string> Respond { get; set; } = _ => "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Request(request.RequestUri!, request.Headers.TryGetValues("X-Plex-Token", out var t) ? t.Single() : null, body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Respond(request)) };
        }
    }

    private sealed class FixedHostConfig : IHostConfigProvider
    {
        public HostSettings Current { get; } = new();

        public bool GeneratedApiKey => false;

        public event EventHandler<HostSettings>? Changed
        {
            add { }
            remove { }
        }

        public bool IsOverriddenByEnvironment(HostConfigKey key) => false;

        public HostSettings Update(HostSettingsUpdate update) => Current;

        public HostSettings ResetApiKey() => Current;
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
