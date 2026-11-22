using Generral.Core.Backup;
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.Download.TrackedDownloads;
using Generral.Core.HealthCheck;
using Generral.Core.IndexerSearch;
using Generral.Core.MediaFiles;
using Generral.Core.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Messaging;

/// <summary>A row of System → Tasks; <see cref="Interval"/> zero means disabled.</summary>
public sealed record ScheduledTask(string Name, string TaskName, TimeSpan Interval, DateTime? LastExecution, DateTime? LastStartTime, DateTime? NextExecution);

/// <summary>Enqueues the scheduled tasks. Disabled with <c>GENERRAL__SCHEDULER__ENABLED=false</c> (used by tests).</summary>
public sealed class ScheduledTaskService(
    CommandQueue queue,
    HealthCheckService health,
    ProviderStatusTracker providerStatus,
    BackupService backups,
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<ScheduledTaskService> logger) : BackgroundService
{
    public const string EnabledKey = "GENERRAL:SCHEDULER:ENABLED";
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan HousekeepingInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan BackupInterval = TimeSpan.FromDays(7);
    public static readonly TimeSpan HealthCheckInterval = TimeSpan.FromHours(6);

    private DateTime? _lastHousekeeping;

    /// <summary>The tasks of docs/specs/02-architecture.md § Command queue & scheduled tasks, with their last and next runs.</summary>
    public async Task<IReadOnlyList<ScheduledTask>> TasksAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var now = time.GetUtcNow().UtcDateTime;
        var sources = await db.Sources.AsNoTracking().Where(s => s.Enabled).ToListAsync(cancellationToken);
        var rssInterval = TimeSpan.FromMinutes((await scope.ServiceProvider.GetRequiredService<IndexerConfigService>().GetAsync(cancellationToken)).RssSyncInterval);

        ScheduledTask Task(string name, string command, TimeSpan interval, DateTime? last = null, DateTime? next = null)
        {
            var run = queue.LastRun(command);
            last ??= run?.Ended;
            next ??= interval > TimeSpan.Zero ? (last ?? now) + interval : null;
            return new ScheduledTask(name, command, interval, last, run?.Started, next);
        }

        var lastBackup = LastScheduledBackup();
        return
        [
            Task("Refresh Sources", RefreshSourcesHandler.CommandName, sources.Count == 0 ? Source.DefaultRefreshInterval : sources.Min(s => s.RefreshInterval)),
            Task("RSS Sync", RssSyncHandler.CommandName, rssInterval, last: ReleaseSearchLastRss(scope.ServiceProvider)),
            Task("Refresh Monitored Downloads", RefreshMonitoredDownloadsHandler.CommandName, TimeSpan.FromMinutes(1)),
            Task("On Air Search", OnAirSearchHandler.CommandName, CheckInterval),
            Task("Housekeeping", CleanUpRecycleBinHandler.CommandName, HousekeepingInterval),
            Task("Backup", BackupHandler.CommandName, BackupInterval, last: lastBackup, next: (lastBackup ?? now) + BackupInterval),
            Task("Check Health", CheckHealthHandler.CommandName, HealthCheckInterval, last: health.LastRun),
        ];
    }

    private static DateTime? ReleaseSearchLastRss(IServiceProvider services) => services.GetRequiredService<ReleaseSearchService>().LastRssSync;

    /// <summary>From the files, so that a restart doesn't trigger a backup.</summary>
    private DateTime? LastScheduledBackup() =>
        backups.All().Where(b => b.Type == BackupType.Scheduled).Select(b => (DateTime?)b.Time).FirstOrDefault();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue(EnabledKey, true))
        {
            logger.LogInformation("Scheduled tasks are disabled");
            return;
        }

        using var timer = new PeriodicTimer(CheckInterval, time);
        do
        {
            try
            {
                await EnqueueDueAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Failed to enqueue scheduled tasks");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EnqueueDueAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var now = time.GetUtcNow().UtcDateTime;
        var sources = await db.Sources.AsNoTracking().Where(s => s.Enabled).ToListAsync(cancellationToken);

        if (sources.Any(s => SourceRefreshService.IsDue(s, now)))
        {
            queue.Enqueue(RefreshSourcesHandler.CommandName, trigger: CommandTrigger.Scheduled);
        }

        var rssInterval = (await scope.ServiceProvider.GetRequiredService<IndexerConfigService>().GetAsync(cancellationToken)).RssSyncInterval;
        var lastRss = scope.ServiceProvider.GetRequiredService<ReleaseSearchService>().LastRssSync;
        if (rssInterval > 0 && (lastRss is null || lastRss.Value.AddMinutes(rssInterval) <= now.AddSeconds(30)))
        {
            queue.Enqueue(RssSyncHandler.CommandName, trigger: CommandTrigger.Scheduled);
        }

        var queued = scope.ServiceProvider.GetRequiredService<TrackedDownloadService>().QueuedEpisodes();
        var onAirRunning = queue.All().Any(c => c.Name == OnAirSearchHandler.CommandName && c.Status is CommandStatus.Queued or CommandStatus.Started);
        if (!onAirRunning && (await OnAirSchedule.DueEpisodesAsync(db, queued, now, cancellationToken)).Count > 0)
        {
            queue.Enqueue(OnAirSearchHandler.CommandName, trigger: CommandTrigger.Scheduled);
        }

        if (_lastHousekeeping is null || _lastHousekeeping + HousekeepingInterval <= now)
        {
            _lastHousekeeping = now;
            queue.Enqueue(CleanUpRecycleBinHandler.CommandName, trigger: CommandTrigger.Scheduled);
        }

        if (await db.DownloadClients.AnyAsync(c => c.Enable, cancellationToken))
        {
            queue.Enqueue(RefreshMonitoredDownloadsHandler.CommandName, trigger: CommandTrigger.Scheduled);
        }

        if (LastScheduledBackup() is not { } lastBackup || lastBackup + BackupInterval <= now)
        {
            queue.Enqueue(BackupHandler.CommandName, trigger: CommandTrigger.Scheduled);
        }

        // Provider outcomes and config changes mark health dirty; the 1-minute tick is Generral's debounce.
        var healthChanged = providerStatus.ConsumeChanged() | health.ConsumeDirty();
        if (health.LastRun is not { } lastHealth || lastHealth + HealthCheckInterval <= now || healthChanged || health.GracePeriodRunDue(now))
        {
            queue.Enqueue(CheckHealthHandler.CommandName, trigger: CommandTrigger.Scheduled);
        }
    }
}
