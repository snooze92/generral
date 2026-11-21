using System.Net.Http.Headers;
using Generral.Core.Lifecycle;
using Generral.Core.Sources.Extraction;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Sources.Http;

public sealed record SourceRequestSettings
{
    /// <summary>Wait between two network requests of one fetch. Cache hits don't count.</summary>
    public TimeSpan RequestDelay { get; init; } = TimeSpan.FromSeconds(1);

    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    public bool BypassCache { get; init; }
}

/// <summary>A summary of a response, shown in the source preview.</summary>
public sealed record RawResponse(Uri Url, int Status, string? ContentType, string Body, bool Truncated, bool FromCache);

/// <summary>Creates the HTTP <see cref="IDocumentFetcher"/> of one fetch.</summary>
public sealed class SourceFetcherFactory(IHttpClientFactory clients, SourceResponseCache cache, TimeProvider time, ILogger<SourceDocumentFetcher> logger)
{
    public const string ClientName = "Source";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    public const long MaxResponseSize = 10 * 1024 * 1024;

    public SourceDocumentFetcher Create(SourceRequestSettings settings) =>
        new(clients.CreateClient(ClientName), cache, time, logger, settings);

    public static void ConfigureClient(HttpClient client)
    {
        client.Timeout = Timeout;
        client.MaxResponseContentBufferSize = MaxResponseSize;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BuildInfo.UserAgent);
    }

    public static SocketsHttpHandler CreateHandler() => new()
    {
        ConnectCallback = SsrfGuard.ConnectCallback(),
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        MaxAutomaticRedirections = 5,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };
}

/// <summary>
/// Fetches source documents over HTTP for one fetch: applies the extra headers, waits the request delay
/// between network requests, serves and fills the raw-response cache, and records a summary of each response.
/// </summary>
public sealed class SourceDocumentFetcher : IDocumentFetcher
{
    public const int BodyLimit = 64 * 1024;

    private readonly HttpClient _client;
    private readonly SourceResponseCache _cache;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SourceRequestSettings _settings;
    private readonly List<RawResponse> _responses = [];
    private bool _requested;

    internal SourceDocumentFetcher(HttpClient client, SourceResponseCache cache, TimeProvider time, ILogger logger, SourceRequestSettings settings)
    {
        _client = client;
        _cache = cache;
        _time = time;
        _logger = logger;
        _settings = settings;
    }

    public IReadOnlyList<RawResponse> Responses => _responses;

    /// <exception cref="HttpRequestException">Network error, blocked address or non-success status.</exception>
    /// <exception cref="TimeoutException">No response within <see cref="SourceFetcherFactory.Timeout"/>.</exception>
    public async Task<FetchedDocument> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        SsrfGuard.EnsureAllowed(url);
        var key = SourceResponseCache.Key(url, _settings.Headers);
        if (!_settings.BypassCache && _cache.TryGet(key, out var cached))
        {
            Record(url, cached, fromCache: true);
            return new FetchedDocument(url, cached.Content);
        }

        if (_requested && _settings.RequestDelay > TimeSpan.Zero)
        {
            await Task.Delay(_settings.RequestDelay, _time, cancellationToken);
        }

        _requested = true;
        var response = await SendAsync(url, cancellationToken);
        Record(url, response, fromCache: false);
        if ((int)response.Status is < 200 or > 299)
        {
            throw new HttpRequestException($"{url.GetLeftPart(UriPartial.Path)} returned HTTP {(int)response.Status}", null, response.Status);
        }

        _cache.Set(key, response);
        return new FetchedDocument(url, response.Content);
    }

    private async Task<CachedResponse> SendAsync(Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (name, value) in _settings.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                throw new HttpRequestException($"Invalid request header '{name}'");
            }
        }

        try
        {
            using var response = await _client.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("GET {Url}: {Status}", url, (int)response.StatusCode);
            return _cache.Create(response.StatusCode, ContentType(response.Content.Headers.ContentType), content);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{url.GetLeftPart(UriPartial.Path)} did not respond within {_client.Timeout.TotalSeconds:0} s", e);
        }
    }

    private void Record(Uri url, CachedResponse response, bool fromCache)
    {
        var truncated = response.Content.Length > BodyLimit;
        _responses.Add(new RawResponse(url, (int)response.Status, response.ContentType,
            truncated ? response.Content[..BodyLimit] : response.Content, truncated, fromCache));
    }

    private static string? ContentType(MediaTypeHeaderValue? value) => value?.MediaType;
}
