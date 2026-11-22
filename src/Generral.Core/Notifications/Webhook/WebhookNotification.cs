// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Notifications/Webhook/Webhook.cs, WebhookBase.cs, WebhookProxy.cs,
// WebhookSettings.cs and WebhookMethod.cs
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.Extensions;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.MediaFiles;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Core.Notifications.Webhook;

public sealed class WebhookNotification(IHttpClientFactory clients, IServiceScopeFactory scopes, IHostConfigProvider hostConfig) : INotification
{
    public const string Name = "Webhook";

    public string Implementation => Name;

    string INotification.Name => Name;

    public string Link => "https://wiki.servarr.com/sonarr/settings#connections";

    public IReadOnlySet<NotificationTrigger> SupportedTriggers { get; } = Enum.GetValues<NotificationTrigger>().ToHashSet();

    public IReadOnlyList<IndexerField> Fields { get; } =
    [
        new() { Order = 0, Name = "url", Label = "Webhook URL", HelpText = "Sonarr-shaped JSON payloads, so existing Sonarr webhook receivers work" },
        new() { Order = 1, Name = "method", Label = "Method", Type = "select", Value = 1, SelectOptions = [new(1, "POST", 1), new(2, "PUT", 2)] },
        new() { Order = 2, Name = "username", Label = "Username", Privacy = "userName" },
        new() { Order = 3, Name = "password", Label = "Password", Type = "password", Privacy = "password" },
    ];

    public IReadOnlyList<IndexerValidationFailure> Validate(JsonObject settings) =>
        Uri.TryCreate(settings.Text("url"), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? settings.Int("method") is null or 1 or 2 ? [] : [new IndexerValidationFailure("method", "Invalid method")]
            : [new IndexerValidationFailure("url", "Must be a valid http(s) URL")];

    public async Task SendAsync(NotificationDefinition definition, NotificationMessage message, CancellationToken cancellationToken = default) =>
        await PostAsync(definition, await BuildAsync(message, cancellationToken), cancellationToken);

    public Task SendHealthAsync(NotificationDefinition definition, HealthCheck.HealthCheck check, bool restored, CancellationToken cancellationToken = default) =>
        PostAsync(definition, new WebhookHealthPayload
        {
            EventType = restored ? WebhookEventType.HealthRestored : WebhookEventType.Health,
            InstanceName = hostConfig.Current.InstanceName,
            Level = check.Type,
            Message = check.Message,
            Type = check.Source,
        }, cancellationToken);

    public async Task<IReadOnlyList<IndexerValidationFailure>> TestAsync(NotificationDefinition definition, CancellationToken cancellationToken = default)
    {
        var payload = new WebhookPayload
        {
            EventType = WebhookEventType.Test,
            InstanceName = hostConfig.Current.InstanceName,
            Series = new WebhookSeries(1, "Test Title", "test-title", "/tv/Test Title", SeriesType.Standard, 0, [], [], ["test-tag"]),
            Episodes = [new WebhookEpisode(123, 1, 1, "Test title", null, null, null, 1)],
        };
        try
        {
            await PostAsync(definition, payload, cancellationToken);
            return [];
        }
        catch (Exception e) when (e is NotificationException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return [new IndexerValidationFailure("url", $"Unable to send test message: {e.Message}")];
        }
    }

    public async Task<WebhookPayload> BuildAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        var series = WebhookSeries.From(message.Series, await TagLabelsAsync(message.Series, cancellationToken));
        var episodes = message.Episodes.Select(WebhookEpisode.From).ToList();
        var instance = hostConfig.Current.InstanceName;
        return message switch
        {
            GrabMessage grab => new WebhookGrabPayload
            {
                EventType = WebhookEventType.Grab,
                InstanceName = instance,
                Series = series,
                Episodes = episodes,
                Release = new WebhookRelease(
                    grab.Remote.Quality.Quality.Name,
                    grab.Remote.Quality.Revision.Version,
                    ReleaseGroupParser.Parse(grab.Remote.Release.Title),
                    grab.Remote.Release.Title,
                    grab.Remote.Release.Indexer,
                    grab.Remote.Release.Size,
                    [.. grab.Remote.Languages]),
                DownloadClient = grab.DownloadClientName,
                DownloadClientType = grab.DownloadClientType,
                DownloadId = grab.DownloadId,
            },
            DownloadMessage download => new WebhookImportPayload
            {
                EventType = WebhookEventType.Download,
                InstanceName = instance,
                Series = series,
                Episodes = episodes,
                EpisodeFile = WebhookEpisodeFile.From(download.File, download.SourcePath),
                IsUpgrade = download.IsUpgrade,
                DownloadClient = download.DownloadClientName,
                DownloadClientType = download.DownloadClientType,
                DownloadId = download.DownloadId,
                DeletedFiles = download.IsUpgrade ? download.OldFiles.Select(f => WebhookEpisodeFile.From(f)).ToList() : null,
                Release = download.Release is { } release ? new WebhookGrabbedRelease(release.Title, release.Indexer, release.Size) : null,
            },
            EpisodeFileDeleteMessage delete => new WebhookEpisodeDeletePayload
            {
                EventType = WebhookEventType.EpisodeFileDelete,
                InstanceName = instance,
                Series = series,
                Episodes = episodes,
                EpisodeFile = WebhookEpisodeFile.From(delete.File),
                DeleteReason = JsonNamingPolicy.CamelCase.ConvertName(delete.Reason),
            },
            SeriesAddMessage => new WebhookSeriesAddPayload { EventType = WebhookEventType.SeriesAdd, InstanceName = instance, Series = series },
            SeriesDeleteMessage seriesDelete => new WebhookSeriesDeletePayload
            {
                EventType = WebhookEventType.SeriesDelete,
                InstanceName = instance,
                Series = series,
                DeletedFiles = seriesDelete.DeletedFiles,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, "Unknown notification message"),
        };
    }

    private async Task<List<string>> TagLabelsAsync(Series series, CancellationToken cancellationToken)
    {
        if (series.Tags.Count == 0)
        {
            return [];
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        return await db.Tags.AsNoTracking().Where(t => series.Tags.Contains(t.Id)).OrderBy(t => t.Label).Select(t => t.Label).ToListAsync(cancellationToken);
    }

    private async Task PostAsync(NotificationDefinition definition, WebhookPayload payload, CancellationToken cancellationToken)
    {
        var settings = definition.Settings;
        using var request = new HttpRequestMessage(settings.Int("method") == 2 ? HttpMethod.Put : HttpMethod.Post, settings.Text("url"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, payload.GetType(), WebhookPayload.JsonOptions), Encoding.UTF8, "application/json"),
        };
        if (settings.Text("username") is { } username)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{settings.Text("password")}")));
        }

        using var response = await clients.CreateClient(NotificationService.ClientName).SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new NotificationException($"Webhook returned HTTP {(int)response.StatusCode}");
        }
    }
}
