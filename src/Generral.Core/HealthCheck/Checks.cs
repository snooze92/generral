// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/HealthCheck/Checks/IndexerRssCheck.cs, IndexerSearchCheck.cs, IndexerStatusCheck.cs,
// DownloadClientCheck.cs, DownloadClientStatusCheck.cs, RootFolderCheck.cs and RemotePathMappingCheck.cs (messages from Localization/Core/en.json)
using Generral.Core.Datastore;
using Generral.Core.Download;
using Generral.Core.Download.TrackedDownloads;
using Generral.Core.Indexers;
using Generral.Core.Lifecycle;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.HealthCheck;

public sealed class IndexerRssCheck(GenerralDbContext db, IndexerStatusService status) : IProvideHealthCheck
{
    public async Task<HealthCheck> CheckAsync(CancellationToken cancellationToken)
    {
        var enabled = await db.Indexers.AsNoTracking().Where(i => i.EnableRss && i.Implementation == IndexerImplementations.Torznab).ToListAsync(cancellationToken);
        if (enabled.Count == 0)
        {
            return HealthCheck.Error(this, $"No indexers available with RSS sync enabled, {BuildInfo.AppName} will not grab new releases automatically");
        }

        var failing = status.Blocked().Select(f => f.IndexerId).ToHashSet();
        return enabled.All(i => failing.Contains(i.Id))
            ? HealthCheck.Warning(this, "All rss-capable indexers are temporarily unavailable due to recent indexer errors")
            : HealthCheck.Ok(this);
    }
}

public sealed class IndexerSearchCheck(GenerralDbContext db, IndexerStatusService status) : IProvideHealthCheck
{
    public async Task<HealthCheck> CheckAsync(CancellationToken cancellationToken)
    {
        var indexers = await db.Indexers.AsNoTracking().Where(i => i.Implementation == IndexerImplementations.Torznab).ToListAsync(cancellationToken);
        var automatic = indexers.Where(i => i.EnableAutomaticSearch).ToList();
        if (automatic.Count == 0)
        {
            return HealthCheck.Warning(this, $"No indexers available with Automatic Search enabled, {BuildInfo.AppName} will not provide any automatic search results");
        }

        if (!indexers.Any(i => i.EnableInteractiveSearch))
        {
            return HealthCheck.Warning(this, $"No indexers available with Interactive Search enabled, {BuildInfo.AppName} will not provide any interactive search results");
        }

        var failing = status.Blocked().Select(f => f.IndexerId).ToHashSet();
        return automatic.All(i => failing.Contains(i.Id))
            ? HealthCheck.Warning(this, "All search-capable indexers are temporarily unavailable due to recent indexer errors")
            : HealthCheck.Ok(this);
    }
}

/// <summary>Indexers in back-off whose first failure is recent (like Sonarr, an indexer failing for longer stops being reported).</summary>
public sealed class IndexerStatusCheck(GenerralDbContext db, IndexerStatusService status, TimeProvider time) : IProvideHealthCheck
{
    public static readonly TimeSpan RecentFailure = TimeSpan.FromHours(6);

    public async Task<HealthCheck> CheckAsync(CancellationToken cancellationToken)
    {
        // Newznab indexers synced by Prowlarr are never queried, so they can't fail.
        var enabled = (await db.Indexers.AsNoTracking().Where(i => i.Implementation == IndexerImplementations.Torznab).ToListAsync(cancellationToken))
            .Where(i => i.Enable).Select(i => i.Id).ToHashSet();
        var since = time.GetUtcNow().UtcDateTime - RecentFailure;
        var failing = status.Blocked().Where(s => enabled.Contains(s.IndexerId) && s.InitialFailure > since).ToList();
        if (failing.Count == 0)
        {
            return HealthCheck.Ok(this);
        }

        return failing.Count == enabled.Count
            ? HealthCheck.Error(this, "All indexers are unavailable due to failures")
            : HealthCheck.Warning(this, $"Indexers unavailable due to failures: {string.Join(", ", failing.Select(f => $"{f.Name} (until {f.DisabledTill:u})"))}");
    }
}

public sealed class DownloadClientCheck(GenerralDbContext db) : IProvideHealthCheck
{
    public async Task<HealthCheck> CheckAsync(CancellationToken cancellationToken) =>
        await db.DownloadClients.AnyAsync(c => c.Enable, cancellationToken)
            ? HealthCheck.Ok(this)
            : HealthCheck.Warning(this, "No download client is available");
}

/// <summary>Sonarr's DownloadClientStatusCheck, from the outcome of the last queue refresh of each client.</summary>
public sealed class DownloadClientStatusCheck(GenerralDbContext db, ProviderStatusTracker status) : IProvideHealthCheck
{
    public async Task<HealthCheck> CheckAsync(CancellationToken cancellationToken)
    {
        var enabled = await db.DownloadClients.AsNoTracking().Where(c => c.Enable).Select(c => c.Id).ToListAsync(cancellationToken);
        var failing = status.Failures(ProviderKind.DownloadClient).Where(f => enabled.Contains(f.Id)).ToList();
        return failing switch
        {
            [] => HealthCheck.Ok(this),
            [var one] => HealthCheck.Error(this, $"Unable to communicate with {one.Name}. {one.Message}"),
            _ when failing.Count == enabled.Count => HealthCheck.Error(this, "All download clients are unavailable due to failures"),
            _ => HealthCheck.Warning(this, $"Download clients unavailable due to failures: {string.Join(", ", failing.Select(f => f.Name))}"),
        };
    }
}

public sealed class RootFolderCheck(GenerralDbContext db) : IProvideHealthCheck
{
    public async Task<HealthCheck> CheckAsync(CancellationToken cancellationToken)
    {
        var missing = (await db.Series.AsNoTracking().Select(s => s.RootFolderPath).Distinct().ToListAsync(cancellationToken))
            .Where(p => !Directory.Exists(p))
            .Order(StringComparer.Ordinal)
            .ToList();
        return missing switch
        {
            [] => HealthCheck.Ok(this),
            [var one] => HealthCheck.Error(this, $"Missing root folder: {one}"),
            _ => HealthCheck.Error(this, $"Multiple root folders are missing: {string.Join(" | ", missing)}"),
        };
    }
}

/// <summary>Sonarr's RemotePathMappingCheck, reduced to its "folder missing" cases, for the downloads the client reports as completed.</summary>
public sealed class RemotePathMappingCheck(TrackedDownloadService tracked, CompletedDownloadService completed, GenerralDbContext db, IRuntimeInfo runtime)
    : IProvideHealthCheck
{
    public async Task<HealthCheck> CheckAsync(CancellationToken cancellationToken)
    {
        var clients = await db.DownloadClients.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
        foreach (var download in tracked.Queue().Where(t => t.Item is { Status: DownloadItemStatus.Completed, OutputPath.Length: > 0 }))
        {
            if (!clients.TryGetValue(download.DownloadClientId, out var client))
            {
                continue;
            }

            var path = await completed.LocalOutputPathAsync(client, download.Item!.OutputPath!, cancellationToken);
            if (File.Exists(path) || Directory.Exists(path))
            {
                continue;
            }

            return HealthCheck.Error(this, runtime.IsContainerized
                ? $"You are using docker; download client {client.Name} places downloads in {path} but this directory does not appear to exist inside the container. Review your remote path mappings and container volume settings."
                : $"Remote download client {client.Name} places downloads in {path} but this directory does not appear to exist. Likely missing or incorrect remote path mapping.");
        }

        return HealthCheck.Ok(this);
    }
}

/// <summary>Generral: sources whose last refresh failed (error once failing for more than <see cref="ErrorAfter"/>).</summary>
public sealed class SourceRefreshCheck(GenerralDbContext db, TimeProvider time) : IProvideHealthCheck
{
    public static readonly TimeSpan ErrorAfter = TimeSpan.FromHours(24);

    public async Task<HealthCheck> CheckAsync(CancellationToken cancellationToken)
    {
        var failing = await db.Sources.AsNoTracking().Where(s => s.Enabled && s.LastError != null).OrderBy(s => s.Name).ToListAsync(cancellationToken);
        if (failing.Count == 0)
        {
            return HealthCheck.Ok(this);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var message = failing.Count == 1
            ? $"Source {failing[0].Name} is failing to refresh: {failing[0].LastError}"
            : $"Sources failing to refresh: {string.Join(", ", failing.Select(s => s.Name))}";
        return failing.Any(s => (s.LastSuccess ?? DateTime.MinValue) + ErrorAfter < now)
            ? HealthCheck.Error(this, message)
            : HealthCheck.Warning(this, message);
    }
}

/// <summary>Generral: mappings whose last apply failed (invalid definition) or left events unmapped because of errors.</summary>
public sealed class MappingCheck(GenerralDbContext db, ProviderStatusTracker status) : IProvideHealthCheck
{
    public async Task<HealthCheck> CheckAsync(CancellationToken cancellationToken)
    {
        var enabled = await db.Mappings.AsNoTracking().Where(m => m.Enabled).Select(m => m.Id).ToListAsync(cancellationToken);
        var failing = status.Failures(ProviderKind.Mapping).Where(f => enabled.Contains(f.Id)).ToList();
        return failing switch
        {
            [] => HealthCheck.Ok(this),
            [var one] => HealthCheck.Warning(this, $"Mapping {one.Name} has errors: {one.Message}"),
            _ => HealthCheck.Warning(this, $"Mappings with errors: {string.Join(", ", failing.Select(f => f.Name))}"),
        };
    }
}
