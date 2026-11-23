using System.Net;
using System.Net.Http.Headers;
using Generral.Core.Extras.Metadata;
using Generral.Core.Lifecycle;
using Generral.Core.Sources.Http;
using Generral.Core.Test.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Core.Test.Extras;

public class ImageDownloaderTests
{
    private ServiceProvider _services = null!;
    private ScriptedHandler _handler = null!;
    private FakeTime _time = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new ScriptedHandler();
        _time = new FakeTime(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc));
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSourceTypes();
        services.AddHttpClient(SourceFetcherFactory.ClientName).ConfigurePrimaryHttpMessageHandler(() => _handler);
        services.AddSingleton<ImageDownloader>();
        _services = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _handler.Dispose();
    }

    private ImageDownloader Downloader => _services.GetRequiredService<ImageDownloader>();

    [Test]
    public async Task Spaces_requests_to_one_host_and_sends_the_user_agent()
    {
        await Downloader.GetAsync(new Uri("https://upload.example.org/a.jpg"), default);
        await Downloader.GetAsync(new Uri("https://upload.example.org/b.jpg"), default);
        await Downloader.GetAsync(new Uri("https://other.example.org/c.jpg"), default);

        Assert.Multiple(() =>
        {
            Assert.That(_time.Delays, Is.EqualTo(new[] { ImageDownloader.RequestInterval }), "only the second request to the same host waits");
            Assert.That(_handler.UserAgents.Distinct(), Is.EqualTo(new[] { BuildInfo.UserAgent }));
        });
    }

    [Test]
    public async Task Honours_a_short_retry_after_once()
    {
        _handler.Responses.Enqueue(() => RateLimited(TimeSpan.FromSeconds(5)));

        var bytes = await Downloader.GetAsync(new Uri("https://upload.example.org/a.jpg"), default);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.Not.Null);
            Assert.That(_handler.Requests, Has.Count.EqualTo(2));
            Assert.That(_time.Delays, Does.Contain(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public async Task Stops_asking_a_host_that_keeps_rate_limiting()
    {
        _handler.Responses.Enqueue(() => RateLimited(TimeSpan.FromSeconds(5)));
        _handler.Responses.Enqueue(() => RateLimited(TimeSpan.FromSeconds(5)));

        var first = await Downloader.GetAsync(new Uri("https://upload.example.org/a.jpg"), default);
        var second = await Downloader.GetAsync(new Uri("https://upload.example.org/b.jpg"), default);
        var other = await Downloader.GetAsync(new Uri("https://other.example.org/c.jpg"), default);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null, "the host is skipped without a request");
            Assert.That(other, Is.Not.Null);
            Assert.That(_handler.Requests, Has.Count.EqualTo(3));
        });

        _time.Advance(ImageDownloader.MaxRetryAfter);
        Assert.That(await Downloader.GetAsync(new Uri("https://upload.example.org/b.jpg"), default), Is.Not.Null, "asked again later");
    }

    [Test]
    public async Task A_long_retry_after_is_not_waited_for()
    {
        _handler.Responses.Enqueue(() => RateLimited(TimeSpan.FromMinutes(10)));

        var bytes = await Downloader.GetAsync(new Uri("https://upload.example.org/a.jpg"), default);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.Null);
            Assert.That(_handler.Requests, Has.Count.EqualTo(1));
            Assert.That(_time.Delays, Is.Empty);
        });
    }

    private static HttpResponseMessage RateLimited(TimeSpan retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
        return response;
    }

    internal sealed class ScriptedHandler : HttpMessageHandler
    {
        public Queue<Func<HttpResponseMessage>> Responses { get; } = new();

        public List<string> Requests { get; } = [];

        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            return Task.FromResult(Responses.TryDequeue(out var next) ? next() : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }
}
