using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Generral.Core.DecisionEngine;
using Generral.Core.Download;
using Generral.Core.Download.Clients.QBittorrent;
using Generral.Core.Indexers;
using Generral.Core.Qualities;
using Generral.Core.Test.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Generral.Core.Test.Download;

public class QBittorrentClientTests
{
    private const string Hash = "0123456789ABCDEF0123456789ABCDEF01234567";

    private FakeQBittorrent _qbit = null!;
    private ServiceProvider _services = null!;
    private QBittorrentClient _client = null!;

    private DownloadClient _definition = null!;

    [SetUp]
    public void SetUp()
    {
        _definition = new DownloadClient
        {
            Id = 1,
            Name = "qBittorrent",
            Implementation = QBittorrentClient.Name,
            Settings = new JsonObject { ["host"] = "qbit.lan", ["port"] = 8111, ["username"] = "admin", ["password"] = "secret" },
        };
        _qbit = new FakeQBittorrent();
        var services = new ServiceCollection();
        services.AddHttpClient(QBittorrentClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => _qbit);
        _services = services.BuildServiceProvider();
        _client = new QBittorrentClient(_services.GetRequiredService<IHttpClientFactory>(), TimeProvider.System, NullLogger<QBittorrentClient>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _qbit.Dispose();
    }

    [Test]
    public async Task Adds_a_magnet_after_logging_in_and_reuses_the_server_cookie()
    {
        var hash = await _client.AddTorrentAsync(_definition, new TorrentPayload(Hash, $"magnet:?xt=urn:btih:{Hash.ToLowerInvariant()}&tr=udp://t", null, "x.torrent"), Remote(), Indexer());

        var add = _qbit.Requests.Single(r => r.Path == "/api/v2/torrents/add");
        Assert.Multiple(() =>
        {
            Assert.That(hash, Is.EqualTo(Hash));
            Assert.That(_qbit.Requests.Count(r => r.Path == "/api/v2/auth/login"), Is.EqualTo(1));
            Assert.That(_qbit.Requests[0].Referer, Is.EqualTo("http://qbit.lan:8111/"));
            Assert.That(_qbit.Requests.Where(r => r.Path != "/api/v2/auth/login").Select(r => r.Cookie), Is.All.EqualTo("QBT_SID_8111=abc"));
            Assert.That(_qbit.Requests.Single(r => r.Path == "/api/v2/torrents/createCategory").Body, Does.Contain("category=generral"));
            Assert.That(add.Body, Does.Contain("name=urls").And.Contain("udp://t"));
            Assert.That(add.Body, Does.Contain("name=category").And.Contain("generral"));
            Assert.That(add.Body, Does.Contain("name=stopped"), "API 2.11 uses 'stopped'");
            Assert.That(add.Body, Does.Contain("name=ratioLimit").And.Contain("1.5"));
        });
    }

    [Test]
    public async Task Uploads_a_torrent_file_and_logs_in_again_on_403()
    {
        _qbit.ExpireSessionOnce = true;
        var file = Encoding.ASCII.GetBytes("d4:infod4:name3:abcee");

        await _client.AddTorrentAsync(_definition, new TorrentPayload(Hash, null, file, "UFC 314.torrent"), Remote(), null);

        var add = _qbit.Requests.Last(r => r.Path == "/api/v2/torrents/add");
        Assert.Multiple(() =>
        {
            Assert.That(_qbit.Requests.Count(r => r.Path == "/api/v2/auth/login"), Is.EqualTo(2));
            Assert.That(add.Body, Does.Contain("name=torrents; filename=\"UFC 314.torrent\"").And.Contain("d4:infod4:name3:abcee"));
            Assert.That(add.Body, Does.Not.Contain("name=urls"));
        });
    }

    [Test]
    public async Task Lists_the_category_and_maps_states()
    {
        var items = await _client.GetItemsAsync(_definition);

        Assert.Multiple(() =>
        {
            Assert.That(_qbit.Requests.Last().PathAndQuery, Is.EqualTo("/api/v2/torrents/info?category=generral"));
            Assert.That(items.Select(i => (i.DownloadId, i.Status, i.IsStalled)), Is.EqualTo(new[]
            {
                ("AAAA", DownloadItemStatus.Downloading, false),
                ("BBBB", DownloadItemStatus.Downloading, true),
                ("CCCC", DownloadItemStatus.Completed, false),
                ("DDDD", DownloadItemStatus.Warning, false),
                ("EEEE", DownloadItemStatus.Queued, false),
                ("FFFF", DownloadItemStatus.Paused, false),
            }));
            Assert.That(items[0].RemainingSize, Is.EqualTo(750));
            Assert.That(items[0].RemainingTime, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(items[2].OutputPath, Is.EqualTo("/downloads/UFC 314"));
            Assert.That(items[2].CanBeRemoved, Is.True, "stopped and ratio reached");
            Assert.That(items[4].Message, Is.EqualTo("Downloading metadata"));
        });
    }

    [Test]
    public async Task Test_reports_bad_credentials_on_the_username()
    {
        _definition.Settings["password"] = "wrong";

        var failures = await _client.TestAsync(_definition);

        Assert.That(failures.Select(f => f.PropertyName), Is.EqualTo(new[] { "username" }));
    }

    [Test]
    public void Infohashes_come_from_magnets_and_torrent_files()
    {
        var torrent = Encoding.ASCII.GetBytes("d8:announce3:url4:infod4:name3:abc6:lengthi5eee");
        var info = Encoding.ASCII.GetBytes("d4:name3:abc6:lengthi5ee");

        Assert.Multiple(() =>
        {
            Assert.That(TorrentInfoHash.FromTorrentFile(torrent), Is.EqualTo(Convert.ToHexString(SHA1.HashData(info))));
            Assert.That(TorrentInfoHash.FromMagnet($"magnet:?dn=x&xt=urn:btih:{Hash.ToLowerInvariant()}"), Is.EqualTo(Hash));
            Assert.That(TorrentInfoHash.FromMagnet("magnet:?xt=urn:btih:AERUKZ4JVPG66AJDIVTYTK6N54ASGRLH"), Is.EqualTo("0123456789ABCDEF0123456789ABCDEF01234567"));
            Assert.That(TorrentInfoHash.FromMagnet("magnet:?dn=nothing"), Is.Null);
            Assert.That(() => TorrentInfoHash.FromTorrentFile(Encoding.ASCII.GetBytes("<html>")), Throws.TypeOf<FormatException>());
        });
    }

    private static RemoteEpisode Remote() => new()
    {
        Release = new ReleaseInfo { Guid = "g", Title = "UFC 314 1080p WEB h264" },
        Quality = new QualityModel { Quality = Quality.WEBDL1080p },
        Episodes = [new Core.Tv.Episode { Id = 1, Title = "Main Card", AirDateUtc = DateTime.UtcNow }],
    };

    private static Indexer Indexer() => new() { Name = "Tracker", Implementation = IndexerImplementations.Torznab, Settings = new JsonObject { ["seedCriteria.seedRatio"] = 1.5 } };

    private sealed record Request(string Path, string PathAndQuery, string? Cookie, string? Referer, string Body);

    /// <summary>A qBittorrent 5 WebUI: login sets <c>QBT_SID_8111</c>, API 2.11.</summary>
    private sealed class FakeQBittorrent : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];

        public bool ExpireSessionOnce { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var uri = request.RequestUri!;
            var cookie = request.Headers.TryGetValues("Cookie", out var c) ? string.Join("; ", c) : null;
            Requests.Add(new Request(uri.AbsolutePath, uri.PathAndQuery, cookie, request.Headers.Referrer?.ToString(), body));

            if (uri.AbsolutePath == "/api/v2/auth/login")
            {
                if (!body.Contains("password=secret", StringComparison.Ordinal))
                {
                    return Text("Fails.");
                }

                var ok = Text("Ok.");
                ok.Headers.Add("Set-Cookie", "QBT_SID_8111=abc; HttpOnly; path=/");
                return ok;
            }

            if (cookie != "QBT_SID_8111=abc" || (ExpireSessionOnce && uri.AbsolutePath == "/api/v2/torrents/add" && Requests.Count(r => r.Path == "/api/v2/auth/login") == 1))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Forbidden") };
            }

            return uri.AbsolutePath switch
            {
                "/api/v2/app/webapiVersion" => Text("2.11.2"),
                "/api/v2/app/preferences" => Text("""{"max_ratio_enabled":true,"max_ratio":1.0,"max_ratio_act":0,"dht":true,"queueing_enabled":false}"""),
                "/api/v2/torrents/categories" => Text("{}"),
                "/api/v2/torrents/createCategory" or "/api/v2/torrents/add" => Text(""),
                "/api/v2/torrents/info" => Text("""
                    [
                      {"hash":"aaaa","name":"UFC 314","size":1000,"progress":0.25,"eta":60,"state":"downloading","category":"generral","save_path":"/downloads/","content_path":"/downloads/UFC 314","ratio":0},
                      {"hash":"bbbb","name":"Stalled","size":1000,"progress":0,"eta":8640000,"state":"stalledDL","category":"generral","save_path":"/downloads/","content_path":"/downloads/Stalled","ratio":0},
                      {"hash":"cccc","name":"UFC 314","size":1000,"progress":1,"eta":8640000,"state":"stoppedUP","category":"generral","save_path":"/downloads/","content_path":"/downloads/UFC 314","ratio":1.2},
                      {"hash":"dddd","name":"Broken","size":1000,"progress":0.5,"eta":0,"state":"error","category":"generral","save_path":"/downloads/","content_path":"/downloads/Broken","ratio":0},
                      {"hash":"eeee","name":"Magnet","size":0,"progress":0,"eta":8640000,"state":"metaDL","category":"generral","save_path":"/downloads/","content_path":"/downloads/","ratio":0},
                      {"hash":"ffff","name":"Paused","size":1000,"progress":0.1,"eta":8640000,"state":"stoppedDL","category":"generral","save_path":"/downloads/","content_path":"/downloads/Paused","ratio":0}
                    ]
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private static HttpResponseMessage Text(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    }
}
