// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Health/HealthController.cs, DiskSpace/DiskSpaceController.cs,
// System/Tasks/TaskController.cs, System/Backup/BackupController.cs, Logs/LogController.cs and Logs/LogFileController.cs
using Generral.Core.Backup;
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.HealthCheck;
using Generral.Core.Instrumentation;
using Generral.Core.Messaging;
using Generral.Core.RootFolders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

public sealed record HealthResource(int Id, string Source, HealthCheckResult Type, string Message, string WikiUrl);

public sealed record DiskSpaceResource(string Path, string Label, long FreeSpace, long TotalSpace);

public sealed record TaskResource(
    int Id, string Name, string TaskName, int Interval, DateTime? LastExecution, DateTime? LastStartTime, DateTime? NextExecution, TimeSpan? LastDuration);

public sealed record BackupResource(int Id, string Name, string Path, BackupType Type, long Size, DateTime Time);

public sealed record LogFileResource(int Id, string Filename, DateTime LastWriteTime, string ContentsUrl, string DownloadUrl);

public sealed record LogResource(int Id, DateTime Time, string Level, string Logger, string Message, string? Exception);

public static class SystemHealthEndpoints
{
    private const long MaxRestoreUpload = 5_000_000_000;

    public static void MapSystemHealthEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/health", (HealthCheckService health) => Health(health));
        api.MapGet("/diskspace", DiskSpaceAsync);

        var tasks = api.MapGroup("/system/task");
        tasks.MapGet("/", async (ScheduledTaskService scheduler, CancellationToken cancellationToken) =>
            (await scheduler.TasksAsync(cancellationToken)).Select(ToResource));
        tasks.MapGet("/{id:int}", async (int id, ScheduledTaskService scheduler, CancellationToken cancellationToken) =>
            (await scheduler.TasksAsync(cancellationToken)).Select(ToResource).FirstOrDefault(t => t.Id == id) is { } task ? Results.Ok(task) : Results.NotFound());

        var backups = api.MapGroup("/system/backup");
        backups.MapGet("/", (BackupService service) => service.All().Select(ToResource));
        backups.MapDelete("/{id:int}", (int id, BackupService service) =>
        {
            if (service.Find(id) is not { } backup)
            {
                return Results.NotFound();
            }

            service.Delete(backup);
            return Results.Ok(new { });
        });
        backups.MapGet("/{id:int}/download", (int id, BackupService service) =>
            service.Find(id) is { } backup ? Results.File(backup.Path, "application/zip", backup.Name) : Results.NotFound());
        backups.MapPost("/restore/{id:int}", (int id, BackupService service) =>
        {
            if (service.Find(id) is not { } backup)
            {
                return Results.NotFound();
            }

            using var stream = File.OpenRead(backup.Path);
            return Restore(service, stream);
        });
        backups.MapPost("/restore/upload", async (HttpRequest request, BackupService service) =>
        {
            if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = MaxRestoreUpload;
            }

            var form = await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = MaxRestoreUpload });
            if (form.Files.Count == 0)
            {
                return ValidationFailure.BadRequest(new ValidationFailure("file", "file must be provided"));
            }

            var file = form.Files[0];
            if (!string.Equals(Path.GetExtension(file.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            }

            // ZipArchive needs a seekable stream.
            await using var stream = new FileStream(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
            await file.CopyToAsync(stream);
            stream.Position = 0;
            return Restore(service, stream);
        }).DisableAntiforgery();

        var logs = api.MapGroup("/log");
        logs.MapGet("/", GetLogs);
        logs.MapGet("/file", (IAppFolders folders, HttpRequest request) => LogFiles(folders, request));
        logs.MapGet("/file/{name}", (string name, IAppFolders folders) =>
        {
            var path = LogFilePath(folders, name);
            if (path is null)
            {
                return Results.NotFound();
            }

            // The file sink writes with shared access, so read the same way.
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Results.Stream(stream, "text/plain");
        });
    }

    internal static IEnumerable<HealthResource> Health(HealthCheckService health) =>
        health.Results().Select((r, i) => new HealthResource(i + 1, r.Source, r.Type, r.Message, ""));

    internal static async Task<List<DiskSpaceResource>> DiskSpaceAsync(GenerralDbContext db, IAppFolders folders, CancellationToken cancellationToken)
    {
        var paths = await db.RootFolders.AsNoTracking().Select(r => r.Path).ToListAsync(cancellationToken);
        paths.Add(folders.ConfigFolder);
        return paths
            .Where(Directory.Exists)
            .Select(p => (Path: p, Drive: TryDrive(p)))
            .Where(d => d.Drive is not null)
            .DistinctBy(d => d.Drive!.RootDirectory.FullName)
            .Select(d => new DiskSpaceResource(d.Drive!.RootDirectory.FullName, d.Drive.VolumeLabel, d.Drive.AvailableFreeSpace, d.Drive.TotalSize))
            .ToList();
    }

    private static DriveInfo? TryDrive(string path)
    {
        var (free, _) = RootFolderService.DiskSpace(path);
        return free is null ? null : new DriveInfo(Path.GetFullPath(path));
    }

    private static IResult Restore(BackupService service, Stream stream)
    {
        try
        {
            service.Restore(stream);
            return Results.Ok(new { restartRequired = true });
        }
        catch (RestoreBackupFailedException e)
        {
            return ValidationFailure.BadRequest(new ValidationFailure("file", e.Message));
        }
    }

    private static TaskResource ToResource(ScheduledTask task, int index) => new(
        index + 1,
        task.Name,
        task.TaskName,
        (int)task.Interval.TotalMinutes,
        task.LastExecution,
        task.LastStartTime,
        task.NextExecution,
        task.LastExecution - task.LastStartTime is { Ticks: >= 0 } duration ? duration : null);

    private static BackupResource ToResource(BackupInfo backup) =>
        new(backup.Id, backup.Name, $"/backup/{JsonNames.Of(backup.Type)}/{backup.Name}", backup.Type, backup.Size, backup.Time);

    private static PagingResource<LogResource> GetLogs(int? page, int? pageSize, string? level, string? sortDirection)
    {
        var current = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 50, 1, 1000);
        var levels = level switch
        {
            "warn" => new[] { "warn", "error", "fatal" },
            "error" => ["error", "fatal"],
            _ => null,
        };
        var entries = RecentLogSink.Instance.All().Where(e => levels is null || levels.Contains(e.Level)).ToList();
        var ascending = sortDirection is "ascending";
        if (ascending)
        {
            entries.Reverse();
        }

        var records = entries.Skip((current - 1) * size).Take(size).Select(e => new LogResource(e.Id, e.Time, e.Level, e.Logger, e.Message, e.Exception)).ToList();
        return new PagingResource<LogResource>(current, size, "time", ascending ? SortDirection.Ascending : SortDirection.Descending, entries.Count, records);
    }

    private static List<LogFileResource> LogFiles(IAppFolders folders, HttpRequest request)
    {
        if (!Directory.Exists(folders.LogsFolder))
        {
            return [];
        }

        var basePath = request.PathBase.Value ?? "";
        return new DirectoryInfo(folders.LogsFolder).EnumerateFiles("*.txt")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select((f, i) => new LogFileResource(
                i + 1,
                f.Name,
                f.LastWriteTimeUtc,
                $"{basePath}/api/v1/log/file/{Uri.EscapeDataString(f.Name)}",
                $"{basePath}/api/v1/log/file/{Uri.EscapeDataString(f.Name)}"))
            .ToList();
    }

    /// <summary>Only plain file names inside the logs folder.</summary>
    private static string? LogFilePath(IAppFolders folders, string name)
    {
        if (name != Path.GetFileName(name) || !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = Path.Combine(folders.LogsFolder, name);
        return File.Exists(path) ? path : null;
    }
}
