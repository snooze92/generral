// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrent.cs, QBittorrentProxyV2.cs,
// QBittorrentTorrent.cs and QBittorrentPreferences.cs
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Generral.Core.DecisionEngine;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.Lifecycle;
using Generral.Core.Sources.Http;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Download.Clients.QBittorrent;

/// <summary>qBittorrent through its WebUI API v2 (qBittorrent 4.1+).</summary>
public sealed class QBittorrentClient(IHttpClientFactory clients, TimeProvider time, ILogger<QBittorrentClient> logger) : IDownloadClient
{
    public const string Name = "QBittorrent";

    /// <summary>The named <see cref="HttpClient"/> of download clients: no automatic redirects or cookies.</summary>
    public const string ClientName = "DownloadClient";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Version StoppedParameterVersion = new(2, 11, 0);

    private readonly ConcurrentDictionary<string, string> _cookies = new();

    public string Implementation => Name;

    public DownloadProtocol Protocol => DownloadProtocol.Torrent;

    public static void ConfigureClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(60);
        client.MaxResponseContentBufferSize = SourceFetcherFactory.MaxResponseSize;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BuildInfo.UserAgent);
    }

    public static SocketsHttpHandler CreateHandler()
    {
        var handler = SourceFetcherFactory.CreateHandler();
        handler.AllowAutoRedirect = false;
        handler.UseCookies = false;
        return handler;
    }

    public IReadOnlyList<IndexerValidationFailure> Validate(JsonObject settings) => QBittorrentSettings.From(settings).Validate();

    public async Task<string> AddTorrentAsync(DownloadClient client, TorrentPayload torrent, RemoteEpisode remoteEpisode, Indexer? indexer, CancellationToken cancellationToken = default)
    {
        var settings = QBittorrentSettings.From(client.Settings);
        if (torrent.MagnetUrl is { } magnet && !magnet.Contains("&tr=", StringComparison.OrdinalIgnoreCase) &&
            !(await GetPreferencesAsync(settings, cancellationToken)).Dht)
        {
            throw new ReleaseDownloadException("Magnet links without trackers are not supported if DHT is disabled");
        }

        await EnsureCategoriesAsync(settings, cancellationToken);
        var version = await GetApiVersionAsync(settings, cancellationToken);
        using var form = new MultipartFormDataContent();
        if (torrent.File is { } file)
        {
            var content = new ByteArrayContent(file);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            form.Add(content, "torrents", torrent.FileName);
        }
        else
        {
            form.Add(new StringContent(torrent.MagnetUrl!), "urls");
        }

        void Add(string name, string value) => form.Add(new StringContent(value), name);
        if (settings.TvCategory is { } category)
        {
            Add("category", category);
        }

        if (settings.InitialState != QBittorrentState.ForceStart)
        {
            Add(version >= StoppedParameterVersion ? "stopped" : "paused", settings.InitialState == QBittorrentState.Stop ? "true" : "false");
        }

        if (settings.SequentialOrder)
        {
            Add("sequentialDownload", "true");
        }

        if (settings.FirstAndLast)
        {
            Add("firstLastPiecePrio", "true");
        }

        if (settings.ContentLayout != QBittorrentContentLayout.Default)
        {
            Add("contentLayout", settings.ContentLayout.ToString());
        }

        if (settings.TorrentTags is { } tags)
        {
            Add("tags", tags);
        }

        var seed = indexer is null ? null : TorznabSettings.From(indexer.Settings);
        if (seed?.SeedRatio is { } ratio)
        {
            Add("ratioLimit", ratio.ToString(CultureInfo.InvariantCulture));
        }

        if (seed?.SeedTime is { } seedTime)
        {
            Add("seedingTimeLimit", seedTime.ToString(CultureInfo.InvariantCulture));
        }

        string result;
        try
        {
            result = await SendAsync(settings, HttpMethod.Post, "/api/v2/torrents/add", () => form, cancellationToken);
        }
        catch (DownloadClientException e) when (e.InnerException is HttpRequestException { StatusCode: HttpStatusCode.Conflict })
        {
            throw new ReleaseDownloadException("qBittorrent rejected the torrent due to a conflict", e);
        }

        if (result.Trim() == "Fails.")
        {
            throw new ReleaseDownloadException("qBittorrent failed to add the torrent");
        }

        var hash = torrent.Hash.ToLowerInvariant();
        var recent = remoteEpisode.Episodes.Any(e => e.AirDateUtc >= time.GetUtcNow().UtcDateTime.Date.AddDays(-14));
        var moveToTop = (recent ? settings.RecentTvPriority : settings.OlderTvPriority) == QBittorrentPriority.First;
        var forceStart = settings.InitialState == QBittorrentState.ForceStart;
        if ((moveToTop || forceStart) && await WaitForTorrentAsync(settings, hash, cancellationToken))
        {
            if (moveToTop)
            {
                await TryAsync(settings, "/api/v2/torrents/topPrio", new() { ["hashes"] = hash }, "set the priority of", cancellationToken);
            }

            if (forceStart)
            {
                await TryAsync(settings, "/api/v2/torrents/setForceStart", new() { ["hashes"] = hash, ["value"] = "true" }, "force start", cancellationToken);
            }
        }

        return torrent.Hash.ToUpperInvariant();
    }

    public async Task<IReadOnlyList<DownloadClientItem>> GetItemsAsync(DownloadClient client, CancellationToken cancellationToken = default)
    {
        var settings = QBittorrentSettings.From(client.Settings);
        var preferences = await GetPreferencesAsync(settings, cancellationToken);
        var path = "/api/v2/torrents/info" + (settings.TvCategory is { } category ? "?category=" + Uri.EscapeDataString(category) : "");
        var torrents = Deserialize<List<QBittorrentTorrent>>(await SendAsync(settings, HttpMethod.Get, path, null, cancellationToken));
        return torrents.Select(t => ToItem(t, preferences, client.RemoveCompletedDownloads)).ToList();
    }

    public async Task RemoveItemAsync(DownloadClient client, string downloadId, bool deleteData, CancellationToken cancellationToken = default)
    {
        var settings = QBittorrentSettings.From(client.Settings);
        await PostFormAsync(settings, "/api/v2/torrents/delete", new() { ["hashes"] = downloadId.ToLowerInvariant(), ["deleteFiles"] = deleteData ? "true" : "false" }, cancellationToken);
    }

    /// <summary>Sonarr's qBittorrent test: connection and authentication, categories, priority support and listing torrents.</summary>
    public async Task<IReadOnlyList<IndexerValidationFailure>> TestAsync(DownloadClient client, CancellationToken cancellationToken = default)
    {
        var settings = QBittorrentSettings.From(client.Settings);
        try
        {
            _cookies.TryRemove(AuthKey(settings), out _);
            var version = await GetApiVersionAsync(settings, cancellationToken);
            if (version < new Version(2, 0))
            {
                return [new("host", $"qBittorrent version {version} is not supported, use qBittorrent 4.1 or newer")];
            }

            var preferences = await GetPreferencesAsync(settings, cancellationToken);
            if (preferences.RemovesCompletedDownloads)
            {
                return [new("", "qBittorrent is configured to remove torrents when they reach their Share Ratio Limit")];
            }

            if (settings.RecentTvPriority == QBittorrentPriority.First || settings.OlderTvPriority == QBittorrentPriority.First)
            {
                if (!preferences.QueueingEnabled)
                {
                    return [new(settings.RecentTvPriority == QBittorrentPriority.First ? "recentTvPriority" : "olderTvPriority", "Queueing not enabled in qBittorrent")];
                }
            }

            if (await EnsureCategoriesAsync(settings, cancellationToken) is { } categoryFailure)
            {
                return [categoryFailure];
            }

            await GetItemsAsync(client, cancellationToken);
            return [];
        }
        catch (DownloadClientAuthenticationException)
        {
            return [new("username", "Authentication failure: check your username and password")];
        }
        catch (DownloadClientException e)
        {
            logger.LogWarning("Unable to connect to qBittorrent: {Message}", e.Message);
            return [new("host", $"Unable to connect to qBittorrent: {e.Message}")];
        }
    }

    private static DownloadClientItem ToItem(QBittorrentTorrent torrent, QBittorrentPreferences preferences, bool removeCompleted)
    {
        var (status, message, stalled) = torrent.State switch
        {
            "error" => (DownloadItemStatus.Warning, "qBittorrent is reporting an error", false),
            "missingFiles" => (DownloadItemStatus.Warning, "qBittorrent is reporting missing files", false),
            "pausedDL" or "stoppedDL" => (DownloadItemStatus.Paused, null, false),
            "queuedDL" or "checkingDL" or "checkingUP" or "checkingResumeData" or "allocating" or "moving" => (DownloadItemStatus.Queued, null, false),
            "metaDL" or "forcedMetaDL" => preferences.Dht
                ? (DownloadItemStatus.Queued, "Downloading metadata", false)
                : (DownloadItemStatus.Warning, "qBittorrent cannot resolve magnet link with DHT disabled", false),
            "downloading" or "forcedDL" => (DownloadItemStatus.Downloading, null, false),
            "stalledDL" => (DownloadItemStatus.Downloading, null, true),
            "uploading" or "stalledUP" or "queuedUP" or "forcedUP" or "pausedUP" or "stoppedUP" => (DownloadItemStatus.Completed, null, false),
            _ => (DownloadItemStatus.Downloading, $"Unknown download state: {torrent.State}", false),
        };

        var completed = status == DownloadItemStatus.Completed;
        string? outputPath = null;
        if (completed)
        {
            outputPath = torrent.RootPath is { Length: > 0 } root && root != torrent.SavePath ? root
                : torrent.ContentPath != torrent.SavePath ? torrent.ContentPath
                : null;
            if (outputPath is null)
            {
                (status, message) = (DownloadItemStatus.Warning, "Unable to import: the download path is the same as the save path");
            }
        }

        return new DownloadClientItem
        {
            DownloadId = torrent.Hash.ToUpperInvariant(),
            Title = torrent.Name,
            Category = torrent.Category,
            TotalSize = torrent.Size,
            RemainingSize = (long)(torrent.Size * (1.0 - torrent.Progress)),
            RemainingTime = completed || torrent.Eta is < 0 or >= 8640000 ? (completed ? TimeSpan.Zero : null) : TimeSpan.FromSeconds(torrent.Eta),
            Status = status,
            Message = message,
            OutputPath = outputPath,
            CanBeRemoved = removeCompleted && torrent.State is "pausedUP" or "stoppedUP" && HasReachedSeedLimit(torrent, preferences),
            CanMoveFiles = removeCompleted && torrent.State is "pausedUP" or "stoppedUP" && HasReachedSeedLimit(torrent, preferences),
            SeedRatio = torrent.Ratio,
            IsStalled = stalled,
        };
    }

    private static bool HasReachedSeedLimit(QBittorrentTorrent torrent, QBittorrentPreferences preferences)
    {
        var ratioLimit = torrent.RatioLimit >= 0 ? torrent.RatioLimit : torrent.RatioLimit == -2 && preferences.MaxRatioEnabled ? preferences.MaxRatio : (double?)null;
        if (ratioLimit is { } ratio && ratio - torrent.Ratio <= 0.001)
        {
            return true;
        }

        var timeLimit = torrent.SeedingTimeLimit >= 0 ? torrent.SeedingTimeLimit : torrent.SeedingTimeLimit == -2 && preferences.MaxSeedingTimeEnabled ? preferences.MaxSeedingTime : (long?)null;
        return timeLimit is { } minutes && torrent.SeedingTime is { } seconds && seconds >= minutes * 60;
    }

    /// <returns>A failure when a category is missing and couldn't be created.</returns>
    private async Task<IndexerValidationFailure?> EnsureCategoriesAsync(QBittorrentSettings settings, CancellationToken cancellationToken)
    {
        var wanted = new[] { ("tvCategory", settings.TvCategory), ("tvImportedCategory", settings.TvImportedCategory) }.Where(c => c.Item2 is not null).ToList();
        if (wanted.Count == 0)
        {
            return null;
        }

        var categories = Deserialize<Dictionary<string, JsonElement>>(await SendAsync(settings, HttpMethod.Get, "/api/v2/torrents/categories", null, cancellationToken));
        foreach (var (field, category) in wanted.Where(c => !categories.ContainsKey(c.Item2!)))
        {
            try
            {
                await PostFormAsync(settings, "/api/v2/torrents/createCategory", new() { ["category"] = category! }, cancellationToken);
            }
            catch (DownloadClientException e)
            {
                return new IndexerValidationFailure(field, $"Configuration of the category failed: {e.Message}");
            }
        }

        return null;
    }

    private async Task<Version> GetApiVersionAsync(QBittorrentSettings settings, CancellationToken cancellationToken)
    {
        var text = await SendAsync(settings, HttpMethod.Get, "/api/v2/app/webapiVersion", null, cancellationToken);
        return Version.TryParse(text.Trim(), out var version) ? version : throw new DownloadClientException($"Unexpected qBittorrent API version '{text.Trim()}'");
    }

    private async Task<QBittorrentPreferences> GetPreferencesAsync(QBittorrentSettings settings, CancellationToken cancellationToken) =>
        Deserialize<QBittorrentPreferences>(await SendAsync(settings, HttpMethod.Get, "/api/v2/app/preferences", null, cancellationToken));

    private async Task<bool> WaitForTorrentAsync(QBittorrentSettings settings, string hash, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await SendAsync(settings, HttpMethod.Get, "/api/v2/torrents/properties?hash=" + hash, null, cancellationToken);
                return true;
            }
            catch (DownloadClientException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), time, cancellationToken);
            }
        }

        logger.LogWarning("Torrent {Hash} not visible in qBittorrent after 1 s, skipping priority and force start", hash);
        return false;
    }

    private async Task TryAsync(QBittorrentSettings settings, string path, Dictionary<string, string> form, string action, CancellationToken cancellationToken)
    {
        try
        {
            await PostFormAsync(settings, path, form, cancellationToken);
        }
        catch (DownloadClientException e) when (e.InnerException is HttpRequestException { StatusCode: HttpStatusCode.Conflict })
        {
            // qBittorrent answers 409 to priority changes when torrent queueing is disabled.
        }
        catch (DownloadClientException e)
        {
            logger.LogWarning("Failed to {Action} {Hash}: {Message}", action, form["hashes"], e.Message);
        }
    }

    private Task<string> PostFormAsync(QBittorrentSettings settings, string path, Dictionary<string, string> form, CancellationToken cancellationToken) =>
        SendAsync(settings, HttpMethod.Post, path, () => new FormUrlEncodedContent(form), cancellationToken);

    /// <summary>Sends an authenticated request, logging in again once on 403.</summary>
    private async Task<string> SendAsync(QBittorrentSettings settings, HttpMethod method, string path, Func<HttpContent>? content, CancellationToken cancellationToken)
    {
        var cookie = await AuthenticateAsync(settings, false, cancellationToken);
        var (status, body) = await RawSendAsync(settings, method, path, content, cookie, cancellationToken);
        if (status == HttpStatusCode.Forbidden && !string.IsNullOrEmpty(settings.Username))
        {
            logger.LogDebug("qBittorrent authentication required, logging in");
            cookie = await AuthenticateAsync(settings, true, cancellationToken);
            (status, body) = await RawSendAsync(settings, method, path, content, cookie, cancellationToken);
        }

        if (status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new DownloadClientAuthenticationException("Failed to authenticate with qBittorrent.");
        }

        if ((int)status is < 200 or >= 300)
        {
            throw new DownloadClientException($"qBittorrent returned HTTP {(int)status} for {path.Split('?')[0]}", new HttpRequestException(null, null, status));
        }

        return body;
    }

    private async Task<string?> AuthenticateAsync(QBittorrentSettings settings, bool force, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.Username) && string.IsNullOrEmpty(settings.Password))
        {
            return null;
        }

        var key = AuthKey(settings);
        if (!force && _cookies.TryGetValue(key, out var cached))
        {
            return cached;
        }

        _cookies.TryRemove(key, out _);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.BaseUrl, Path(settings, "/api/v2/auth/login")))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = settings.Username ?? "", ["password"] = settings.Password ?? "" }),
        };
        request.Headers.Referrer = settings.BaseUrl;
        using var response = await SendRequestAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized || (body.Length > 0 && body.Trim() != "Ok."))
        {
            throw new DownloadClientAuthenticationException("Failed to authenticate with qBittorrent.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new DownloadClientException($"qBittorrent login returned HTTP {(int)response.StatusCode}");
        }

        // The cookie is SID, or QBT_SID_<port> on newer builds: whatever the server sets is sent back.
        var cookie = string.Join("; ", (response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [])
            .Select(v => v.Split(';')[0].Trim())
            .Where(v => v.Contains('=')));
        _cookies[key] = cookie;
        return cookie;
    }

    private async Task<(HttpStatusCode Status, string Body)> RawSendAsync(QBittorrentSettings settings, HttpMethod method, string path, Func<HttpContent>? content, string? cookie, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(settings.BaseUrl, Path(settings, path))) { Content = content?.Invoke() };
        request.Headers.Referrer = settings.BaseUrl;
        if (!string.IsNullOrEmpty(cookie))
        {
            request.Headers.Add("Cookie", cookie);
        }

        using var response = await SendRequestAsync(request, cancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            SsrfGuard.EnsureAllowed(request.RequestUri!);
            var response = await clients.CreateClient(ClientName).SendAsync(request, cancellationToken);
            logger.LogDebug("{Method} {Path}: {Status}", request.Method, request.RequestUri!.AbsolutePath, (int)response.StatusCode);
            return response;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new DownloadClientException($"Failed to connect to qBittorrent: {e.Message}", e);
        }
    }

    private static string Path(QBittorrentSettings settings, string path) => settings.BaseUrl.AbsolutePath.TrimEnd('/') + path;

    private static string AuthKey(QBittorrentSettings settings) => $"{settings.BaseUrl}:{settings.Username}:{settings.Password}";

    private static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new DownloadClientException("Empty response from qBittorrent");
        }
        catch (JsonException e)
        {
            throw new DownloadClientException("Invalid response from qBittorrent", e);
        }
    }

    private sealed record QBittorrentTorrent
    {
        public string Hash { get; init; } = "";

        public string Name { get; init; } = "";

        public long Size { get; init; }

        public double Progress { get; init; }

        public long Eta { get; init; }

        public string State { get; init; } = "";

        public string? Category { get; init; }

        [JsonPropertyName("save_path")]
        public string? SavePath { get; init; }

        [JsonPropertyName("root_path")]
        public string? RootPath { get; init; }

        [JsonPropertyName("content_path")]
        public string? ContentPath { get; init; }

        public double Ratio { get; init; }

        [JsonPropertyName("ratio_limit")]
        public double RatioLimit { get; init; } = -2;

        [JsonPropertyName("seeding_time")]
        public long? SeedingTime { get; init; }

        [JsonPropertyName("seeding_time_limit")]
        public long SeedingTimeLimit { get; init; } = -2;
    }

    private sealed record QBittorrentPreferences
    {
        [JsonPropertyName("max_ratio_enabled")]
        public bool MaxRatioEnabled { get; init; }

        [JsonPropertyName("max_ratio")]
        public double MaxRatio { get; init; }

        [JsonPropertyName("max_seeding_time_enabled")]
        public bool MaxSeedingTimeEnabled { get; init; }

        [JsonPropertyName("max_seeding_time")]
        public long MaxSeedingTime { get; init; }

        /// <summary>0 pause, 1 remove, 2 super seeding, 3 remove with files.</summary>
        [JsonPropertyName("max_ratio_act")]
        public int MaxRatioAction { get; init; }

        [JsonPropertyName("queueing_enabled")]
        public bool QueueingEnabled { get; init; }

        public bool Dht { get; init; } = true;

        public bool RemovesCompletedDownloads =>
            (MaxRatioEnabled || (MaxSeedingTimeEnabled && MaxSeedingTime < 60 * 24 * 14)) && MaxRatioAction is 1 or 3;
    }
}
