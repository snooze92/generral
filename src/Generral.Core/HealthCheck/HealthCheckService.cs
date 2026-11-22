// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/HealthCheck/HealthCheckService.cs and CheckHealthCommand.cs
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Generral.Core.Lifecycle;
using Generral.Core.Messaging;
using Generral.Core.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Generral.Core.HealthCheck;

/// <summary>
/// Runs every check and keeps the failing results in memory. Like Sonarr, issues found during the first
/// <see cref="StartupGracePeriod"/> are not notified; the first run after it notifies every current issue.
/// </summary>
public sealed class HealthCheckService(IServiceScopeFactory scopes, IRuntimeInfo runtime, TimeProvider time, ILogger<HealthCheckService> logger)
{
    public static readonly TimeSpan StartupGracePeriod = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, HealthCheck> _results = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _notifiedAfterGracePeriod;
    private int _dirty;

    public DateTime? LastRun { get; private set; }

    public IReadOnlyList<HealthCheck> Results() =>
        _results.Values.OrderByDescending(r => r.Type).ThenBy(r => r.Source, StringComparer.Ordinal).ToList();

    /// <summary>Asks for a run at the next scheduler check (indexers, download clients, root folders, … changed).</summary>
    public void MarkDirty() => Interlocked.Exchange(ref _dirty, 1);

    public bool ConsumeDirty() => Interlocked.Exchange(ref _dirty, 0) == 1;

    /// <summary>True once the grace period ended and its catch-up run has not happened yet.</summary>
    public bool GracePeriodRunDue(DateTime now) => !_notifiedAfterGracePeriod && now >= runtime.StartTime + StartupGracePeriod;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopes.CreateScope();
            var now = time.GetUtcNow().UtcDateTime;
            var graceOver = now >= runtime.StartTime + StartupGracePeriod;
            var notifyAll = graceOver && !_notifiedAfterGracePeriod;
            var failed = new List<HealthCheck>();
            var restored = new List<HealthCheck>();

            foreach (var check in scope.ServiceProvider.GetServices<IProvideHealthCheck>())
            {
                var result = await CheckAsync(check, cancellationToken);
                if (result.Type == HealthCheckResult.Ok)
                {
                    if (_results.TryRemove(result.Source, out var previous))
                    {
                        restored.Add(previous);
                    }
                }
                else
                {
                    var previous = _results.GetValueOrDefault(result.Source);
                    _results[result.Source] = result;
                    if (previous is null || notifyAll)
                    {
                        failed.Add(result);
                    }
                }
            }

            LastRun = now;
            if (!graceOver)
            {
                return;
            }

            _notifiedAfterGracePeriod = true;
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
            foreach (var result in failed)
            {
                await notifications.NotifyHealthAsync(result, restored: false, cancellationToken);
            }

            foreach (var result in restored)
            {
                await notifications.NotifyHealthAsync(result, restored: true, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HealthCheck> CheckAsync(IProvideHealthCheck check, CancellationToken cancellationToken)
    {
        try
        {
            return await check.CheckAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Health check {Check} failed", check.GetType().Name);
            return HealthCheck.Ok(check);
        }
    }
}

public sealed class CheckHealthHandler(HealthCheckService health) : ICommandHandler
{
    public const string CommandName = "CheckHealth";

    public string Name => CommandName;

    public async Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken)
    {
        await health.RunAsync(cancellationToken);
        var count = health.Results().Count;
        return count == 0 ? "No health issues" : $"{count} health issue(s)";
    }
}
