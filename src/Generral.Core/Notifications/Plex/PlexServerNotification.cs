// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Notifications/Plex/Server/PlexServer.cs, PlexServerService.cs, PlexServerProxy.cs,
// PlexServerSettings.cs, PlexSection.cs and PlexResponse.cs
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Generral.Core.Extensions;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.Lifecycle;
using Generral.Core.Tv;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Notifications.Plex;

public sealed record PlexSectionLocation(int Id, string Path);

public sealed record PlexSection(
    [property: JsonPropertyName("key"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] int Id,
    string Type,
    string? Agent,
    [property: JsonPropertyName("Location")] List<PlexSectionLocation>? Locations);

/// <summary>
/// Plex Media Server "Update Library": a partial scan of the series folder in the TV section whose location contains it
/// (spec 09 § Providers). The token is pasted (no plex.tv sign-in yet).
/// </summary>
public sealed class PlexServerNotification(IHttpClientFactory clients, ILogger<PlexServerNotification> logger) : IMediaServerNotification
{
    public const string Name = "PlexServer";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Implementation => Name;

    string INotification.Name => "Plex Media Server";

    public string Link => "https://www.plex.tv/";

    public IReadOnlySet<NotificationTrigger> SupportedTriggers { get; } = new HashSet<NotificationTrigger>
    {
        NotificationTrigger.Download, NotificationTrigger.Upgrade, NotificationTrigger.SeriesAdd, NotificationTrigger.SeriesDelete,
        NotificationTrigger.EpisodeFileDelete, NotificationTrigger.EpisodeFileDeleteForUpgrade,
    };

    public IReadOnlyList<IndexerField> Fields { get; } =
    [
        new() { Order = 0, Name = "host", Label = "Host", Value = "" },
        new() { Order = 1, Name = "port", Label = "Port", Type = "number", Value = 32400 },
        new() { Order = 2, Name = "useSsl", Label = "Use SSL", Type = "checkbox", Value = false, HelpText = "Connect to Plex over HTTPS instead of HTTP" },
        new() { Order = 3, Name = "urlBase", Label = "URL Base", Advanced = true, HelpText = "Adds a prefix to the Plex url, such as http://[host]:[port]/[urlBase]/plex" },
        new()
        {
            Order = 4, Name = "authToken", Label = "Auth Token", Privacy = "apiKey",
            HelpText = "Your X-Plex-Token: in Plex Web open any item → ⋯ → Get Info → View XML, and copy the X-Plex-Token value from the URL",
        },
        new() { Order = 5, Name = "updateLibrary", Label = "Update Library", Type = "checkbox", Value = true, HelpText = "Partial scan of the series folder on import, rename, upgrade and delete" },
        new() { Order = 6, Name = "mapFrom", Label = "Map Paths From", Advanced = true, HelpText = "Generral path, used to modify series paths when Plex sees library path location differently from Generral (requires 'Update Library')" },
        new() { Order = 7, Name = "mapTo", Label = "Map Paths To", Advanced = true, HelpText = "Plex path, used to modify series paths when Plex sees library path location differently from Generral (requires 'Update Library')" },
    ];

    public IReadOnlyList<IndexerValidationFailure> Validate(JsonObject settings)
    {
        var failures = new List<IndexerValidationFailure>();
        if (settings.Text("host") is null)
        {
            failures.Add(new IndexerValidationFailure("host", "Host is required"));
        }

        if (settings.Int("port") is { } port and (< 1 or > 65535))
        {
            failures.Add(new IndexerValidationFailure("port", "Must be between 1 and 65535"));
        }

        if ((settings.Text("mapFrom") is null) != (settings.Text("mapTo") is null))
        {
            failures.Add(new IndexerValidationFailure("mapTo", "'Map Paths From' and 'Map Paths To' go together"));
        }

        return failures;
    }

    public async Task SendAsync(NotificationDefinition definition, NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (definition.Settings.Bool("updateLibrary", true))
        {
            await UpdateLibraryAsync(definition, message.Series, cancellationToken);
        }
    }

    public async Task UpdateLibraryAsync(NotificationDefinition definition, Series series, CancellationToken cancellationToken = default)
    {
        var settings = definition.Settings;
        var sections = await GetTvSectionsAsync(settings, cancellationToken);
        var path = MapPath(settings, series.Path);
        var matches = sections.Where(s => (s.Locations ?? []).Any(l => Contains(l.Path, path))).ToList();
        if (matches.Count == 0)
        {
            logger.LogDebug("Plex has no TV library covering {Path}; not refreshing", path);
            return;
        }

        foreach (var section in matches)
        {
            logger.LogDebug("Updating Plex section {Section} for {Path}", section.Id, path);
            await GetAsync(settings, $"library/sections/{section.Id.ToString(CultureInfo.InvariantCulture)}/refresh", path, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<IndexerValidationFailure>> TestAsync(NotificationDefinition definition, CancellationToken cancellationToken = default)
    {
        try
        {
            return (await GetTvSectionsAsync(definition.Settings, cancellationToken)).Count == 0
                ? [new IndexerValidationFailure("host", "At least one TV library is required")]
                : [];
        }
        catch (PlexAuthenticationException)
        {
            return [new IndexerValidationFailure("authToken", "Invalid authentication token")];
        }
        catch (Exception e) when (e is NotificationException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return [new IndexerValidationFailure("host", $"Unable to connect to Plex Media Server: {e.Message}")];
        }
    }

    public async Task<List<PlexSection>> GetTvSectionsAsync(JsonObject settings, CancellationToken cancellationToken)
    {
        var body = await GetAsync(settings, "library/sections", null, cancellationToken);
        try
        {
            var directories = JsonNode.Parse(body)?["MediaContainer"]?["Directory"]?.Deserialize<List<PlexSection>>(JsonOptions) ?? [];
            return directories.Where(d => d.Type == "show").ToList();
        }
        catch (JsonException e)
        {
            throw new NotificationException("Plex returned an invalid library list", e);
        }
    }

    /// <summary>Generral's series path as Plex sees it, through the optional path mapping.</summary>
    public static string MapPath(JsonObject settings, string path)
    {
        if (settings.Text("mapFrom") is { } from && settings.Text("mapTo") is { } to && Contains(from, path))
        {
            var relative = path[from.TrimEnd('/', '\\').Length..].TrimStart('/', '\\');
            var separator = to.Contains('\\', StringComparison.Ordinal) ? '\\' : '/';
            return relative.Length == 0 ? to.TrimEnd('/', '\\') : $"{to.TrimEnd('/', '\\')}{separator}{relative.Replace('/', separator).Replace('\\', separator)}";
        }

        return path;
    }

    private static bool Contains(string folder, string path)
    {
        var trimmed = folder.TrimEnd('/', '\\');
        var comparison = trimmed.Contains('\\', StringComparison.Ordinal) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.Equals(trimmed, comparison) ||
               (path.StartsWith(trimmed, comparison) && path.Length > trimmed.Length && path[trimmed.Length] is '/' or '\\');
    }

    private async Task<string> GetAsync(JsonObject settings, string resource, string? path, CancellationToken cancellationToken)
    {
        var urlBase = settings.Text("urlBase") is { } b ? "/" + b.Trim('/') : "";
        var query = $"X-Plex-Client-Identifier={BuildInfo.AppName}&X-Plex-Product={BuildInfo.AppName}&X-Plex-Version={Uri.EscapeDataString(BuildInfo.Version)}";
        if (path is not null)
        {
            query += "&path=" + Uri.EscapeDataString(path);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{(settings.Bool("useSsl") ? "https" : "http")}://{settings.Text("host")}:{settings.Int("port") ?? 32400}{urlBase}/{resource}?{query}");
        request.Headers.Accept.ParseAdd("application/json");
        if (settings.Text("authToken") is { } token)
        {
            request.Headers.Add("X-Plex-Token", token);
        }

        using var response = await clients.CreateClient(NotificationService.ClientName).SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new PlexAuthenticationException("Unauthorized - AuthToken is invalid");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new NotificationException($"Unable to connect to Plex Media Server. Status Code: {(int)response.StatusCode}");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}

public sealed class PlexAuthenticationException(string message) : NotificationException(message);
