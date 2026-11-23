using System.Collections.Concurrent;
using System.Net;
using Generral.Core.Sources.Http;

namespace Generral.Core.Extras.Metadata;

/// <summary>
/// Downloads metadata images one at a time per host, at most one request per <see cref="RequestInterval"/>. A 429/503 is retried
/// once after a short <c>Retry-After</c>; otherwise the host is left alone for a while (Wikimedia and the like rate limit bursts).
/// </summary>
public sealed class ImageDownloader(IHttpClientFactory clients, TimeProvider time)
{
    public static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxBlock = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRequest = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _blockedUntil = new(StringComparer.OrdinalIgnoreCase);

    /// <returns>The image bytes, or null when the host is rate limiting.</returns>
    /// <exception cref="HttpRequestException">Any other failure.</exception>
    public async Task<byte[]?> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        var host = uri.Host;
        var gate = _gates.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                if (_blockedUntil.TryGetValue(host, out var until) && until > time.GetUtcNow())
                {
                    return null;
                }

                if (_lastRequest.TryGetValue(host, out var last) && last + RequestInterval - time.GetUtcNow() is { } wait && wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, time, cancellationToken);
                }

                _lastRequest[host] = time.GetUtcNow();
                using var response = await clients.CreateClient(SourceFetcherFactory.ClientName).GetAsync(uri, cancellationToken);
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    var retryAfter = RetryAfter(response);
                    if (attempt == 0 && retryAfter is { } delay && delay <= MaxRetryAfter)
                    {
                        await Task.Delay(delay, time, cancellationToken);
                        continue;
                    }

                    var block = retryAfter is { } r ? TimeSpan.FromTicks(Math.Clamp(r.Ticks, MaxRetryAfter.Ticks, MaxBlock.Ticks)) : MaxRetryAfter;
                    _blockedUntil[host] = time.GetUtcNow() + block;
                    return null;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - time.GetUtcNow() is var d && d > TimeSpan.Zero ? d : TimeSpan.Zero,
        _ => null,
    };
}
