using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Generral.Core.Sources.Http;
using Generral.Core.Sources.Types;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Core.Test.Sources;

/// <summary>The source types as registered in DI, with a fake network and clock.</summary>
internal sealed class SourceTestHost : IDisposable
{
    private readonly ServiceProvider _services;

    public SourceTestHost()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSourceTypes();
        services.AddHttpClient(SourceFetcherFactory.ClientName).ConfigurePrimaryHttpMessageHandler(() => Handler);
        _services = services.BuildServiceProvider();
    }

    public FakeHandler Handler { get; } = new();

    public FakeTime Time { get; } = new(new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc));

    public SourceTypeRegistry Types => _services.GetRequiredService<SourceTypeRegistry>();

    public SourceFetcherFactory Fetchers => _services.GetRequiredService<SourceFetcherFactory>();

    public Task<SourceFetchResult> FetchAsync(string type, JsonObject settings, SourceFetchOptions? options = null) =>
        Types.Get(type).FetchAsync(settings, options);

    public static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name));

    public void Dispose() => _services.Dispose();
}

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, (HttpStatusCode Status, string ContentType, string Body)> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> RequestedUrls => Requests.Select(r => r.RequestUri!.AbsoluteUri).ToList();

    public FakeHandler Add(string url, string body, string contentType = "application/json", HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses[new Uri(url).AbsoluteUri] = (status, contentType, body);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        var response = _responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var r)
            ? new HttpResponseMessage(r.Status) { Content = new StringContent(r.Body, null, r.ContentType) }
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") };
        return Task.FromResult(response);
    }
}

/// <summary>A clock that only moves when told to. Timers fire at once, and their due times are recorded.</summary>
internal sealed class FakeTime(DateTime utcNow) : TimeProvider
{
    private DateTimeOffset _now = new(utcNow, TimeSpan.Zero);

    public List<TimeSpan> Delays { get; } = [];

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (Delays)
        {
            Delays.Add(dueTime);
        }

        _now += dueTime;
        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new NoopTimer();
    }

    private sealed class NoopTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
