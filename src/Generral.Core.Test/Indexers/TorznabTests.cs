using System.Text.Json.Nodes;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.Test.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Generral.Core.Test.Indexers;

public class TorznabTests
{
    private static readonly Uri RequestUrl = new("https://prowlarr.lan/1/api?t=search");

    [Test]
    public void Capabilities_are_parsed()
    {
        var caps = TorznabParser.ParseCapabilities(SourceTestHost.Fixture("torznab/caps.xml"));

        Assert.Multiple(() =>
        {
            Assert.That(caps.DefaultPageSize, Is.EqualTo(50));
            Assert.That(caps.MaxPageSize, Is.EqualTo(200));
            Assert.That(caps.SupportedSearchParameters, Is.EqualTo(new[] { "q" }));
            Assert.That(caps.SupportedTvSearchParameters, Is.EqualTo(new[] { "q", "season", "ep" }));
            Assert.That(caps.Categories.Select(c => c.Id), Is.EqualTo(new[] { 2000, 5000 }));
            Assert.That(caps.Categories[1].Subcategories.Select(c => c.Name), Is.EqualTo(new[] { "TV/HD", "TV/Sport" }));
        });
    }

    [Test]
    public void Search_results_are_parsed_to_releases()
    {
        var indexer = new Indexer { Id = 7, Name = "Tracker (Prowlarr)", Implementation = "Torznab", Priority = 10 };

        var releases = TorznabParser.ParseReleases(SourceTestHost.Fixture("torznab/search.xml"), RequestUrl, indexer);

        Assert.That(releases, Has.Count.EqualTo(2), "the item without a link is skipped");
        var (main, prelims) = (releases[0], releases[1]);
        Assert.Multiple(() =>
        {
            Assert.That(main.Title, Is.EqualTo("UFC 300 Pereira vs Hill 1080p WEB h264-GRP"));
            Assert.That(main.Guid, Is.EqualTo("https://tracker.example/details/101"));
            Assert.That(main.DownloadUrl, Is.EqualTo("https://prowlarr.lan/1/download?id=101"));
            Assert.That(main.InfoUrl, Is.EqualTo("https://tracker.example/details/101"));
            Assert.That(main.Size, Is.EqualTo(4294967296));
            Assert.That(main.Seeders, Is.EqualTo(120));
            Assert.That(main.Peers, Is.EqualTo(135));
            Assert.That(main.InfoHash, Is.EqualTo("0123456789ABCDEF0123456789ABCDEF01234567"));
            Assert.That(main.PublishDate, Is.EqualTo(new DateTime(2024, 4, 14, 6, 12, 0, DateTimeKind.Utc)));
            Assert.That(main.PublishDate.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(main.Categories, Is.EqualTo(new[] { 5000, 5060 }));
            Assert.That(main.IndexerFlags, Is.EqualTo(IndexerFlags.Freeleech | IndexerFlags.DoubleUpload | IndexerFlags.Internal));
            Assert.That((main.IndexerId, main.Indexer, main.IndexerPriority), Is.EqualTo((7, "Tracker (Prowlarr)", 10)));

            Assert.That(prelims.Title, Is.EqualTo("UFC 300 Prelims 720p & Early Prelims"));
            Assert.That(prelims.DownloadUrl, Is.Null);
            Assert.That(prelims.MagnetUrl, Does.StartWith("magnet:?xt=urn:btih:89ABCDEF"));
            Assert.That(prelims.Size, Is.EqualTo(1073741824));
            Assert.That(prelims.Peers, Is.EqualTo(15), "seeders + leechers");
            Assert.That(prelims.PublishDate, Is.EqualTo(new DateTime(2024, 4, 14, 2, 0, 0, DateTimeKind.Utc)), "unknown zone names are dropped");
            Assert.That(prelims.IndexerFlags, Is.EqualTo(IndexerFlags.Halfleech));
        });
    }

    [TestCase("<error code=\"100\" description=\"Incorrect user credentials\"/>", true, "Invalid API key")]
    [TestCase("<error code=\"500\" description=\"Request limit reached\"/>", false, "API limit reached")]
    [TestCase("<html>nope", false, "Invalid XML")]
    public void Errors_are_reported(string xml, bool apiKeyError, string message)
    {
        var e = Assert.Throws<TorznabException>(() => TorznabParser.ParseReleases(xml, RequestUrl))!;

        Assert.Multiple(() =>
        {
            Assert.That(e.ApiKeyError, Is.EqualTo(apiKeyError));
            Assert.That(e.Message, Does.StartWith(message));
        });
    }

    [Test]
    public async Task Search_builds_the_torznab_request_and_test_reads_the_caps()
    {
        var handler = new FakeHandler()
            .Add("http://prowlarr.lan:9696/1/api?t=search&cat=5000,5060&extended=1&q=UFC%20300&offset=100&limit=50&apikey=k%2By", SourceTestHost.Fixture("torznab/search.xml"), "application/rss+xml")
            .Add("http://prowlarr.lan:9696/1/api?t=caps&apikey=k%2By", SourceTestHost.Fixture("torznab/caps.xml"), "application/xml");
        var services = new ServiceCollection();
        services.AddHttpClient(TorznabClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var client = new TorznabClient(provider.GetRequiredService<IHttpClientFactory>(), new FakeTime(DateTime.UtcNow), NullLogger<TorznabClient>.Instance);
        var settings = new JsonObject { ["baseUrl"] = "http://prowlarr.lan:9696/1/", ["apiPath"] = "/api", ["apiKey"] = "k+y", ["categories"] = new JsonArray(5000, 5060) };
        var indexer = new Indexer { Name = "Tracker", Implementation = "Torznab", Settings = settings };

        var releases = await client.SearchAsync(indexer, new TorznabSearch { Query = "UFC 300", Offset = 100, Limit = 50 });
        var test = await client.TestAsync(TorznabSettings.From(settings));
        var unreachable = await client.TestAsync(TorznabSettings.From(new JsonObject { ["baseUrl"] = "http://other.lan/" }));

        Assert.Multiple(() =>
        {
            Assert.That(releases, Has.Count.EqualTo(2));
            Assert.That(test, Is.Null);
            Assert.That(unreachable?.ErrorMessage, Does.StartWith("Unable to connect to indexer"));
        });
    }
}
