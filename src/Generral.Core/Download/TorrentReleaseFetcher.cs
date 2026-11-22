// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Download/TorrentClientBase.cs
using System.Net;
using System.Text.RegularExpressions;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.Sources.Http;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Download;

/// <summary>What is sent to the torrent client: a magnet link or the <c>.torrent</c> file, with its infohash.</summary>
public sealed record TorrentPayload(string Hash, string? MagnetUrl, byte[]? File, string FileName);

/// <summary>
/// Fetches the <c>.torrent</c> through the indexer's download link (so indexer credentials never reach the client); a redirect to a
/// <c>magnet:</c> is passed on as a magnet (spec 07 § .torrent vs magnet).
/// </summary>
public sealed partial class TorrentReleaseFetcher(IHttpClientFactory clients, ILogger<TorrentReleaseFetcher> logger)
{
    public const string ClientName = "IndexerDownload";

    private const int MaxRedirects = 5;

    /// <summary>Redirects are followed by hand: the handler would throw on a <c>magnet:</c> location.</summary>
    public static SocketsHttpHandler CreateHandler()
    {
        var handler = SourceFetcherFactory.CreateHandler();
        handler.AllowAutoRedirect = false;
        return handler;
    }

    /// <exception cref="ReleaseDownloadException">Neither the torrent file nor the magnet link is usable.</exception>
    public async Task<TorrentPayload> ResolveAsync(ReleaseInfo release, CancellationToken cancellationToken = default)
    {
        var downloadUrl = release.DownloadUrl;
        var magnet = downloadUrl?.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) == true ? downloadUrl : release.MagnetUrl;
        var torrentUrl = downloadUrl?.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) == true ? null : downloadUrl;
        var fileName = FileName(release.Title);

        if (!string.IsNullOrWhiteSpace(torrentUrl))
        {
            try
            {
                return await FetchAsync(release, new Uri(torrentUrl), fileName, 0, cancellationToken);
            }
            catch (ReleaseDownloadException e) when (!string.IsNullOrWhiteSpace(magnet))
            {
                logger.LogWarning("Downloading the torrent file of {Title} failed, using the magnet link: {Message}", release.Title, e.Message);
            }
        }

        if (string.IsNullOrWhiteSpace(magnet))
        {
            throw new ReleaseDownloadException($"{release.Title} has no download link");
        }

        return FromMagnet(release, magnet, fileName);
    }

    private async Task<TorrentPayload> FetchAsync(ReleaseInfo release, Uri url, string fileName, int redirects, CancellationToken cancellationToken)
    {
        if (url.Scheme is not ("http" or "https"))
        {
            throw new ReleaseDownloadException($"Unsupported download link scheme '{url.Scheme}'");
        }

        byte[] content;
        try
        {
            SsrfGuard.EnsureAllowed(url);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/x-bittorrent");
            using var response = await clients.CreateClient(ClientName).SendAsync(request, cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location ?? throw new ReleaseDownloadException("Remote website tried to redirect without providing a location.");
                if (location.OriginalString.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    return FromMagnet(release, location.OriginalString, fileName);
                }

                return redirects < MaxRedirects
                    ? await FetchAsync(release, new Uri(url, location), fileName, redirects + 1, cancellationToken)
                    : throw new ReleaseDownloadException("Too many redirects");
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                throw new ReleaseDownloadException($"Downloading torrent failed: the release no longer exists (HTTP {(int)response.StatusCode})");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ReleaseDownloadException($"Downloading torrent failed: {url.GetLeftPart(UriPartial.Path)} returned HTTP {(int)response.StatusCode}");
            }

            content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new ReleaseDownloadException($"Downloading torrent failed: {e.Message}", e);
        }

        try
        {
            return new TorrentPayload(TorrentInfoHash.FromTorrentFile(content), null, content, fileName);
        }
        catch (FormatException e)
        {
            throw new ReleaseDownloadException($"Downloading torrent failed: {e.Message}", e);
        }
    }

    private static TorrentPayload FromMagnet(ReleaseInfo release, string magnet, string fileName)
    {
        var hash = TorrentInfoHash.FromMagnet(magnet) ?? (release.InfoHash is { Length: 40 } h ? h.ToUpperInvariant() : null);
        return hash is null
            ? throw new ReleaseDownloadException($"Failed to parse the magnet link of {release.Title}: no infohash")
            : new TorrentPayload(hash, magnet, null, fileName);
    }

    private static string FileName(string title) => InvalidFileNameCharacters().Replace(title, "") + ".torrent";

    [GeneratedRegex(@"[\\/:*?""<>|\x00-\x1F]")]
    private static partial Regex InvalidFileNameCharacters();
}
