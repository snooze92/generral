using System.Runtime.InteropServices;
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.Lifecycle;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

public sealed record SystemStatusResource
{
    public required string AppName { get; init; }

    public required string InstanceName { get; init; }

    public required string Version { get; init; }

    public string? Commit { get; init; }

    public DateTime BuildTime { get; init; }

    public bool IsDebug { get; init; }

    public bool IsProduction { get; init; }

    public bool IsContainerized { get; init; }

    public bool IsLinux { get; init; }

    public bool IsOsx { get; init; }

    public bool IsWindows { get; init; }

    public required string OsName { get; init; }

    public required string OsVersion { get; init; }

    public required string Mode { get; init; }

    public required string Authentication { get; init; }

    public required string DatabaseType { get; init; }

    public required string DatabaseVersion { get; init; }

    public required string MigrationVersion { get; init; }

    public required string UrlBase { get; init; }

    public required string RuntimeName { get; init; }

    public required string RuntimeVersion { get; init; }

    public DateTime StartTime { get; init; }

    public required string StartupPath { get; init; }

    public required string AppData { get; init; }
}

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder api)
    {
        var system = api.MapGroup("/system");

        system.MapGet("/status", GetStatus);

        system.MapPost("/restart", (IRestartService restart) =>
        {
            restart.RequestRestart();

            return Results.Ok(new { restarting = true });
        });
    }

    private static SystemStatusResource GetStatus(
        IHostConfigProvider config,
        IAppFolders folders,
        IRuntimeInfo runtime,
        IHostEnvironment environment,
        GenerralDbContext db)
    {
        var settings = config.Current;
        var connection = db.Database.GetDbConnection();

        return new SystemStatusResource
        {
            AppName = BuildInfo.AppName,
            InstanceName = settings.InstanceName,
            Version = BuildInfo.Version,
            Commit = BuildInfo.Commit,
            BuildTime = BuildInfo.BuildTime,
            IsDebug = BuildInfo.IsDebug,
            IsProduction = environment.IsProduction(),
            IsContainerized = runtime.IsContainerized,
            IsLinux = OperatingSystem.IsLinux(),
            IsOsx = OperatingSystem.IsMacOS(),
            IsWindows = OperatingSystem.IsWindows(),
            OsName = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.VersionString,
            Mode = "console",
            Authentication = JsonNames.Of(settings.AuthenticationMethod),
            DatabaseType = JsonNames.Of(settings.DatabaseType),
            DatabaseVersion = connection.ServerVersion ?? GetSqliteVersion(db),
            MigrationVersion = db.Database.GetAppliedMigrations().LastOrDefault() ?? "none",
            UrlBase = settings.UrlBase,
            RuntimeName = ".NET",
            RuntimeVersion = Environment.Version.ToString(),
            StartTime = runtime.StartTime,
            StartupPath = AppContext.BaseDirectory,
            AppData = folders.ConfigFolder,
        };
    }

    private static string GetSqliteVersion(GenerralDbContext db)
    {
        return db.Database.SqlQueryRaw<string>("SELECT sqlite_version() AS Value").AsEnumerable().FirstOrDefault() ?? "unknown";
    }
}
