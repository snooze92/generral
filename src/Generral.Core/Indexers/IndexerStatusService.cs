// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/ThingiProvider/Status/ProviderStatusServiceBase.cs, EscalationBackOff.cs,
// ProviderStatusBase.cs and src/NzbDrone.Core/Indexers/IndexerStatusService.cs
using Generral.Core.HealthCheck;
using Generral.Core.Lifecycle;

namespace Generral.Core.Indexers;

public sealed record IndexerStatus(int IndexerId, string Name, int EscalationLevel, DateTime? InitialFailure, DateTime? MostRecentFailure, DateTime? DisabledTill);

/// <summary>
/// Sonarr's indexer back-off: each failure escalates the period an indexer is skipped by RSS sync and searches, each success
/// steps it down again. In memory (a restart clears it), and failures are also reported to <see cref="ProviderStatusTracker"/>.
/// </summary>
public sealed class IndexerStatusService(ProviderStatusTracker tracker, IRuntimeInfo runtime, TimeProvider time)
{
    public static readonly IReadOnlyList<TimeSpan> Periods =
    [
        TimeSpan.Zero,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(24),
    ];

    /// <summary>During this time after a start, a failure disables an indexer for at most <c>Periods[2]</c> and doesn't escalate.</summary>
    public static readonly TimeSpan MinimumTimeSinceStartup = TimeSpan.FromMinutes(15);

    private readonly Lock _lock = new();
    private readonly Dictionary<int, IndexerStatus> _statuses = [];

    public bool IsDisabled(int indexerId)
    {
        lock (_lock)
        {
            return _statuses.TryGetValue(indexerId, out var status) && IsDisabled(status);
        }
    }

    public IReadOnlyList<IndexerStatus> Blocked()
    {
        lock (_lock)
        {
            return _statuses.Values.Where(IsDisabled).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public void RecordSuccess(Indexer indexer)
    {
        lock (_lock)
        {
            if (_statuses.TryGetValue(indexer.Id, out var status) && status.EscalationLevel > 0)
            {
                _statuses[indexer.Id] = status with { EscalationLevel = status.EscalationLevel - 1, DisabledTill = null };
            }
        }

        tracker.RecordSuccess(ProviderKind.Indexer, indexer.Id);
    }

    /// <param name="minimumBackOff">The indexer asked to be left alone this long (capped at the longest period).</param>
    /// <param name="escalate">False for connection failures, like Sonarr's <c>RecordConnectionFailure</c>.</param>
    public IndexerStatus RecordFailure(Indexer indexer, string message, TimeSpan minimumBackOff = default, bool escalate = true)
    {
        IndexerStatus status;
        lock (_lock)
        {
            var now = time.GetUtcNow().UtcDateTime;
            status = _statuses.GetValueOrDefault(indexer.Id) ?? new IndexerStatus(indexer.Id, indexer.Name, 0, null, null, null);
            status = status with { Name = indexer.Name, MostRecentFailure = now };
            if (status.EscalationLevel == 0)
            {
                status = status with { InitialFailure = now, EscalationLevel = 1 };
                escalate = false;
            }

            var inStartupGracePeriod = runtime.StartTime + MinimumTimeSinceStartup > now;
            if (escalate && !inStartupGracePeriod)
            {
                status = status with { EscalationLevel = Math.Min(Periods.Count - 1, status.EscalationLevel + 1) };
            }

            var disabledTill = now + Periods[status.EscalationLevel];
            if (minimumBackOff > TimeSpan.Zero)
            {
                var requested = now + (minimumBackOff < Periods[^1] ? minimumBackOff : Periods[^1]);
                disabledTill = requested > disabledTill ? requested : disabledTill;
            }
            else if (inStartupGracePeriod && now + Periods[2] < disabledTill)
            {
                disabledTill = now + Periods[2];
            }

            status = _statuses[indexer.Id] = status with { DisabledTill = disabledTill };
        }

        tracker.RecordFailure(ProviderKind.Indexer, indexer.Id, indexer.Name, message);
        return status;
    }

    public void Remove(int indexerId)
    {
        lock (_lock)
        {
            _statuses.Remove(indexerId);
        }

        tracker.RecordSuccess(ProviderKind.Indexer, indexerId);
    }

    private bool IsDisabled(IndexerStatus status) => status.DisabledTill > time.GetUtcNow().UtcDateTime;
}
