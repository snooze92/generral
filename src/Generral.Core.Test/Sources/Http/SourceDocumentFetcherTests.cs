using System.Net;
using Generral.Core.Lifecycle;
using Generral.Core.Sources.Http;

namespace Generral.Core.Test.Sources.Http;

public class SourceDocumentFetcherTests
{
    private const string Url = "https://api.example.org/events";

    private SourceTestHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new SourceTestHost();
        _host.Handler.Add(Url, "[1]").Add(Url + "?page=2", "[2]").Add(Url + "?page=3", "[3]");
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public async Task Sends_the_user_agent_and_extra_headers()
    {
        var fetcher = _host.Fetchers.Create(new SourceRequestSettings { Headers = [new("X-Token", "abc")] });

        var document = await fetcher.FetchAsync(new Uri(Url), CancellationToken.None);

        var request = _host.Handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(document.Content, Is.EqualTo("[1]"));
            Assert.That(request.Headers.UserAgent.ToString(), Is.EqualTo(BuildInfo.UserAgent));
            Assert.That(request.Headers.GetValues("X-Token").Single(), Is.EqualTo("abc"));
            Assert.That(fetcher.Responses.Single(), Is.EqualTo(new RawResponse(new Uri(Url), 200, "application/json", "[1]", false, false)));
        });
    }

    [Test]
    public async Task Serves_repeated_requests_from_the_cache_for_an_hour()
    {
        await _host.Fetchers.Create(new SourceRequestSettings()).FetchAsync(new Uri(Url), CancellationToken.None);

        var cached = _host.Fetchers.Create(new SourceRequestSettings());
        await cached.FetchAsync(new Uri(Url), CancellationToken.None);
        await _host.Fetchers.Create(new SourceRequestSettings { Headers = [new("X-Token", "other")] }).FetchAsync(new Uri(Url), CancellationToken.None);
        await _host.Fetchers.Create(new SourceRequestSettings { BypassCache = true }).FetchAsync(new Uri(Url), CancellationToken.None);
        var afterBypass = _host.Handler.Requests.Count;
        _host.Time.Advance(SourceResponseCache.Lifetime);
        await _host.Fetchers.Create(new SourceRequestSettings()).FetchAsync(new Uri(Url), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(cached.Responses.Single().FromCache, Is.True);
            Assert.That(afterBypass, Is.EqualTo(3), "first fetch, other headers, bypass");
            Assert.That(_host.Handler.Requests, Has.Count.EqualTo(4), "expired");
        });
    }

    [Test]
    public async Task Waits_the_request_delay_between_network_requests_only()
    {
        await _host.Fetchers.Create(new SourceRequestSettings()).FetchAsync(new Uri(Url), CancellationToken.None);
        var fetcher = _host.Fetchers.Create(new SourceRequestSettings { RequestDelay = TimeSpan.FromMilliseconds(1500) });

        await fetcher.FetchAsync(new Uri(Url), CancellationToken.None);
        await fetcher.FetchAsync(new Uri(Url + "?page=2"), CancellationToken.None);
        await fetcher.FetchAsync(new Uri(Url + "?page=3"), CancellationToken.None);

        Assert.That(_host.Time.Delays, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(1500) }), "no delay for the cache hit nor the first network request");
    }

    [Test]
    public void Non_success_responses_throw_and_are_not_cached()
    {
        var fetcher = _host.Fetchers.Create(new SourceRequestSettings { RequestDelay = TimeSpan.Zero });

        var e = Assert.ThrowsAsync<HttpRequestException>(() => fetcher.FetchAsync(new Uri(Url + "/missing"), CancellationToken.None));
        Assert.ThrowsAsync<HttpRequestException>(() => fetcher.FetchAsync(new Uri(Url + "/missing"), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(e!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(_host.Handler.Requests, Has.Count.EqualTo(2));
            Assert.That(fetcher.Responses.Select(r => r.Status), Is.EqualTo(new[] { 404, 404 }));
        });
    }

    [Test]
    public async Task Truncates_large_bodies_in_the_summary()
    {
        _host.Handler.Add(Url + "/large", new string('x', SourceDocumentFetcher.BodyLimit + 10));
        var fetcher = _host.Fetchers.Create(new SourceRequestSettings());

        var document = await fetcher.FetchAsync(new Uri(Url + "/large"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(document.Content, Has.Length.EqualTo(SourceDocumentFetcher.BodyLimit + 10));
            Assert.That(fetcher.Responses.Single().Body, Has.Length.EqualTo(SourceDocumentFetcher.BodyLimit));
            Assert.That(fetcher.Responses.Single().Truncated, Is.True);
        });
    }

    [TestCase("http://169.254.169.254/latest/meta-data/")]
    [TestCase("http://[fe80::1]/")]
    [TestCase("http://[::ffff:169.254.169.254]/")]
    public void Link_local_literals_are_blocked_before_any_request(string url)
    {
        var fetcher = _host.Fetchers.Create(new SourceRequestSettings());

        var e = Assert.ThrowsAsync<HttpRequestException>(() => fetcher.FetchAsync(new Uri(url), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(e!.Message, Does.Contain("link-local"));
            Assert.That(_host.Handler.Requests, Is.Empty);
        });
    }
}

public class SsrfGuardTests
{
    [TestCase("169.254.169.254", true)]
    [TestCase("169.254.0.1", true)]
    [TestCase("fe80::1", true)]
    [TestCase("febf::1", true)]
    [TestCase("::ffff:169.254.1.1", true)]
    [TestCase("169.253.255.255", false)]
    [TestCase("10.0.0.1", false)]
    [TestCase("192.168.1.10", false)]
    [TestCase("127.0.0.1", false)]
    [TestCase("fec0::1", false)]
    [TestCase("2001:db8::1", false)]
    public void Blocks_link_local_addresses(string address, bool blocked)
    {
        Assert.That(SsrfGuard.IsBlocked(IPAddress.Parse(address)), Is.EqualTo(blocked));
    }

    [Test]
    public void Names_resolving_only_to_link_local_addresses_are_not_connected_to()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = SsrfGuard.ConnectCallback((_, _) => Task.FromResult(new[] { IPAddress.Parse("169.254.169.254"), IPAddress.Parse("fe80::1") })),
        };
        using var client = new HttpClient(handler);

        var e = Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://metadata.example.internal/"));

        Assert.That(e!.ToString(), Does.Contain("Requests to link-local addresses are blocked (metadata.example.internal)"));
    }

    [Test]
    public async Task Allowed_addresses_are_connected_to()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync();
            var buffer = new byte[4096];
            _ = await socket.ReceiveAsync(buffer);
            await socket.SendAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray());
        });
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = SsrfGuard.ConnectCallback((_, _) => Task.FromResult(new[] { IPAddress.Parse("169.254.169.254"), IPAddress.Loopback })),
        };
        using var client = new HttpClient(handler);

        var body = await client.GetStringAsync($"http://events.example.org:{port}/");
        await server;

        Assert.That(body, Is.EqualTo("ok"));
    }
}
