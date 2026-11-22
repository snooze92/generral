using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.HealthCheck;
using Generral.Core.Lifecycle;
using Microsoft.AspNetCore.Routing;

namespace Generral.Http.Api;

/// <summary>The Sonarr v3 compatibility shim (docs/specs/11-api.md § Sonarr compatibility shim): what Prowlarr's Sonarr app sync calls.</summary>
public static class SonarrShimEndpoints
{
    public const string Prefix = "/api/v3";

    /// <summary>
    /// The version reported to Sonarr clients: major 4 (Sonarr v4, as Prowlarr requires ≥ 3.0.5), then Generral's
    /// major.minor.patch, e.g. Generral 0.1.0 → <c>4.0.1.0</c>. Always parses as a <see cref="System.Version"/>.
    /// </summary>
    public static string Version { get; } = ShimVersion(BuildInfo.Version);

    public static void MapSonarrShimEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/system/status", (IHostConfigProvider config, IAppFolders folders, IRuntimeInfo runtime, IHostEnvironment environment, GenerralDbContext db) =>
            SystemEndpoints.GetStatus(config, folders, runtime, environment, db) with { Version = Version, GenerralVersion = BuildInfo.Version });
        api.MapGet("/health", (HealthCheckService health) => SystemHealthEndpoints.Health(health));
        api.MapGet("/diskspace", SystemHealthEndpoints.DiskSpaceAsync);

        api.MapIndexerEndpoints();
        api.MapTagEndpoints();
    }

    internal static string ShimVersion(string version)
    {
        var numeric = version.Split('-', '+')[0];
        return System.Version.TryParse(numeric, out var v)
            ? $"4.{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}"
            : "4.0.0.0";
    }
}
