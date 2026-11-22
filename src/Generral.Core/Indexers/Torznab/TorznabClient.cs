// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Indexers/Torznab/Torznab.cs, src/NzbDrone.Core/Indexers/HttpIndexerBase.cs
// and src/NzbDrone.Core/Indexers/Newznab/NewznabRequestGenerator.cs
using System.Collections.Concurrent;
using System.Globalization;
using Generral.Core.Lifecycle;
using Generral.Core.Sources.Http;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Indexers.Torznab;

public sealed record TorznabSearch
{
    /// <summary>Null for an RSS fetch (latest releases).</summary>
    public string? Query { get; init; }

    /// <summary>Overrides the indexer's categories.</summary>
    public IReadOnlyList<int>? Categories { get; init; }

    public int Offset { get; init; }

    /// <summary>Null for the indexer's default page size.</summary>
    public int? Limit { get; init; }
}

/// <summary>Talks Torznab to one indexer: <c>t=caps</c>, <c>t=search</c> (RSS is a search without <c>q</c>).</summary>
public sealed class TorznabClient(IHttpClientFactory clients, TimeProvider time, ILogger<TorznabClient> logger)
{
    public const string ClientName = "Indexer";

    /// <summary>Minimum gap between two requests to the same indexer host.</summary>
    public static readonly TimeSpan RateLimit = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hostLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRequest = new(StringComparer.OrdinalIgnoreCase);

    public static void ConfigureClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(100);
        client.MaxResponseContentBufferSize = SourceFetcherFactory.MaxResponseSize;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BuildInfo.UserAgent);
    }

    /// <exception cref="TorznabException">The indexer reported an error, or the response isn't Torznab.</exception>
    /// <exception cref="HttpRequestException">Network error, blocked address or non-success status.</exception>
    public async Task<TorznabCapabilities> GetCapabilitiesAsync(TorznabSettings settings, CancellationToken cancellationToken = default)
    {
        var url = settings.RequestUrl("t=caps");
        return TorznabParser.ParseCapabilities(await GetAsync(url, cancellationToken));
    }

    /// <exception cref="TorznabException">The indexer reported an error, or the response isn't Torznab.</exception>
    /// <exception cref="HttpRequestException">Network error, blocked address or non-success status.</exception>
    public async Task<IReadOnlyList<ReleaseInfo>> SearchAsync(Indexer indexer, TorznabSearch search, CancellationToken cancellationToken = default)
    {
        var settings = TorznabSettings.From(indexer.Settings);
        var categories = string.Join(",", (search.Categories ?? [.. settings.Categories, .. settings.AnimeCategories]).Distinct());
        var query = $"t=search&cat={categories}&extended=1";
        if (!string.IsNullOrWhiteSpace(search.Query))
        {
            query += "&q=" + Uri.EscapeDataString(search.Query);
        }

        if (search.Offset > 0)
        {
            query += "&offset=" + search.Offset.ToString(CultureInfo.InvariantCulture);
        }

        if (search.Limit is { } limit)
        {
            query += "&limit=" + limit.ToString(CultureInfo.InvariantCulture);
        }

        var url = settings.RequestUrl(query);
        return TorznabParser.ParseReleases(await GetAsync(url, cancellationToken), url, indexer);
    }

    /// <summary>Sonarr's indexer test: the caps must be readable and allow a text search.</summary>
    public async Task<IndexerValidationFailure?> TestAsync(TorznabSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var caps = await GetCapabilitiesAsync(settings, cancellationToken);
            if (caps.SupportedSearchParameters?.Contains("q") == true ||
                (caps.SupportedTvSearchParameters is { } tv && tv.Contains("q")))
            {
                return null;
            }

            return new IndexerValidationFailure("", "Indexer does not support the required search parameters");
        }
        catch (TorznabException e) when (e.ApiKeyError)
        {
            return new IndexerValidationFailure("apiKey", "Invalid API key");
        }
        catch (Exception e) when (e is TorznabException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Unable to connect to indexer: {Message}", e.Message);
            return new IndexerValidationFailure("", $"Unable to connect to indexer: {e.Message}");
        }
    }

    public Task<IReadOnlyList<ReleaseInfo>> FetchRssAsync(Indexer indexer, CancellationToken cancellationToken = default) =>
        SearchAsync(indexer, new TorznabSearch(), cancellationToken);

    private async Task<string> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        SsrfGuard.EnsureAllowed(url);
        var host = url.Authority;
        var gate = _hostLocks.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_lastRequest.TryGetValue(host, out var last) && last + RateLimit - time.GetUtcNow() is { } wait && wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, time, cancellationToken);
            }

            _lastRequest[host] = time.GetUtcNow();
            using var response = await clients.CreateClient(ClientName).GetAsync(url, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogDebug("GET {Url}: {Status}", url, (int)response.StatusCode);

            var isXml = response.Content.Headers.ContentType?.MediaType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true;
            if (!response.IsSuccessStatusCode && !isXml)
            {
                throw new HttpRequestException($"{url.GetLeftPart(UriPartial.Path)} returned HTTP {(int)response.StatusCode}", null, response.StatusCode);
            }

            return content;
        }
        finally
        {
            gate.Release();
        }
    }
}
