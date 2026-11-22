// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Notifications/NotificationService.cs and NotificationFactory.cs
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.Lifecycle;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Notifications;

/// <summary>Sends an event to every connection that has its trigger on and shares a tag with the series (or has none). Never throws.</summary>
public sealed class NotificationService(
    GenerralDbContext db,
    NotificationProvider provider,
    IUiSettingsService uiSettings,
    TimeProvider time,
    ILogger<NotificationService> logger)
{
    public const string ClientName = "Notification";

    public static void ConfigureClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BuildInfo.UserAgent);
    }

    public async Task NotifyAsync(NotificationTrigger trigger, NotificationMessage message, CancellationToken cancellationToken = default)
    {
        var spoilerFree = uiSettings.Get().SpoilerFreeMode;
        var now = time.GetUtcNow().UtcDateTime;
        message = message with { Episodes = message.Episodes.Select(e => SpoilerFilter.Apply(e, spoilerFree, now)).ToList() };
        foreach (var (definition, notification) in await TargetsAsync(message.Series, cancellationToken))
        {
            if (!definition.Has(trigger) || !notification.SupportedTriggers.Contains(trigger))
            {
                continue;
            }

            await RunAsync(definition, () => notification.SendAsync(definition, message, cancellationToken), cancellationToken);
        }
    }

    /// <summary>Health issues go to every connection with the trigger, whatever its tags; warnings only with <c>includeHealthWarnings</c>.</summary>
    public async Task NotifyHealthAsync(HealthCheck.HealthCheck check, bool restored, CancellationToken cancellationToken = default)
    {
        var trigger = restored ? NotificationTrigger.HealthRestored : NotificationTrigger.HealthIssue;
        foreach (var definition in await db.Notifications.AsNoTracking().ToListAsync(cancellationToken))
        {
            if (provider.Find(definition.Implementation) is not { } notification || !definition.Has(trigger) || !notification.SupportedTriggers.Contains(trigger) ||
                !restored && check.Type != HealthCheck.HealthCheckResult.Error && !(check.Type == HealthCheck.HealthCheckResult.Warning && definition.IncludeHealthWarnings))
            {
                continue;
            }

            await RunAsync(definition, () => notification.SendHealthAsync(definition, check, restored, cancellationToken), cancellationToken);
        }
    }

    /// <summary>Refreshes media servers after only metadata files changed (e.g. a title update).</summary>
    public async Task UpdateMediaServersAsync(Series series, CancellationToken cancellationToken = default)
    {
        foreach (var (definition, notification) in await TargetsAsync(series, cancellationToken))
        {
            if (notification is IMediaServerNotification server)
            {
                await RunAsync(definition, () => server.UpdateLibraryAsync(definition, series, cancellationToken), cancellationToken);
            }
        }
    }

    private async Task<List<(NotificationDefinition Definition, INotification Notification)>> TargetsAsync(Series series, CancellationToken cancellationToken)
    {
        var definitions = await db.Notifications.AsNoTracking().ToListAsync(cancellationToken);
        return definitions
            .Where(d => d.Tags.Count == 0 || d.Tags.Intersect(series.Tags).Any())
            .Select(d => (d, provider.Find(d.Implementation)!))
            .Where(t => t.Item2 is not null)
            .ToList();
    }

    private async Task RunAsync(NotificationDefinition definition, Func<Task> send, CancellationToken cancellationToken)
    {
        try
        {
            await send();
        }
        catch (Exception e) when (e is NotificationException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Unable to send notification to {Name}: {Error}", definition.Name, e.Message);
        }
    }
}

/// <summary>The connection implementations.</summary>
public sealed class NotificationProvider(IEnumerable<INotification> implementations)
{
    private readonly Dictionary<string, INotification> _implementations = implementations.ToDictionary(i => i.Implementation, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<INotification> All => _implementations.Values;

    public INotification? Find(string? implementation) =>
        implementation is not null && _implementations.TryGetValue(implementation, out var notification) ? notification : null;
}

/// <summary>Spoiler-safe display helpers shared by the providers.</summary>
public static class NotificationFormat
{
    /// <summary><c>UFC - S2025E04 - UFC 314</c> (year seasons read better as SxxEyy than Sonarr's <c>2025x04</c>), or <c>Series - date - title</c> for daily series.</summary>
    public static string Title(Series series, IReadOnlyList<Episode> episodes)
    {
        if (episodes.Count == 0)
        {
            return series.Title;
        }

        if (series.SeriesType == SeriesType.Daily)
        {
            var episode = episodes[0];
            return $"{series.Title} - {episode.AirDate:yyyy-MM-dd} - {episode.Title}";
        }

        var numbers = string.Concat(episodes.Select(e => $"E{e.EpisodeNumber:00}"));
        return $"{series.Title} - S{episodes[0].SeasonNumber:00}{numbers} - {string.Join(" + ", episodes.Select(e => e.Title))}";
    }
}
