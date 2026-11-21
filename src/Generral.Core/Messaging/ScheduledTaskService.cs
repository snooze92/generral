using Generral.Core.Datastore;
using Generral.Core.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Messaging;

/// <summary>Enqueues the scheduled tasks. Disabled with <c>GENERRAL__SCHEDULER__ENABLED=false</c> (used by tests).</summary>
public sealed class ScheduledTaskService(
    CommandQueue queue,
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<ScheduledTaskService> logger) : BackgroundService
{
    public const string EnabledKey = "GENERRAL:SCHEDULER:ENABLED";
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

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
    }
}
