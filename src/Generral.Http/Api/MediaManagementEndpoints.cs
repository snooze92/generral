// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/RootFolders/RootFolderController.cs, RemotePathMappings/RemotePathMappingController.cs,
// Config/NamingConfigController.cs, Config/NamingExampleResource.cs, Config/MediaManagementConfigController.cs, EpisodeFiles/EpisodeFileController.cs,
// EpisodeFiles/EpisodeFileResource.cs and src/NzbDrone.Core/Organizer/FileNameSampleService.cs
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.DecisionEngine;
using Generral.Core.Languages;
using Generral.Core.MediaFiles;
using Generral.Core.Organizer;
using Generral.Core.Qualities;
using Generral.Core.RemotePathMappings;
using Generral.Core.RootFolders;
using Generral.Core.Tv;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

public sealed record UnmappedFolderResource(string Name, string Path, string RelativePath);

public sealed record RootFolderResource
{
    public int Id { get; init; }

    public string? Path { get; init; }

    public bool Accessible { get; init; }

    public long? FreeSpace { get; init; }

    public long? TotalSpace { get; init; }

    public List<UnmappedFolderResource> UnmappedFolders { get; init; } = [];

    public static RootFolderResource From(RootFolderInfo info) => new()
    {
        Id = info.Folder.Id,
        Path = info.Folder.Path,
        Accessible = info.Accessible,
        FreeSpace = info.FreeSpace,
        TotalSpace = info.TotalSpace,
        UnmappedFolders = info.UnmappedFolders.Select(f => new UnmappedFolderResource(System.IO.Path.GetFileName(f), f, System.IO.Path.GetRelativePath(info.Folder.Path, f))).ToList(),
    };
}

public sealed record RemotePathMappingResource
{
    public int Id { get; init; }

    public string? Host { get; init; }

    public string? RemotePath { get; init; }

    public string? LocalPath { get; init; }

    public static RemotePathMappingResource From(RemotePathMapping mapping) =>
        new() { Id = mapping.Id, Host = mapping.Host, RemotePath = mapping.RemotePath, LocalPath = mapping.LocalPath };
}

public sealed record NamingExampleResource(string? SingleEpisodeExample, string? MultiEpisodeExample, string? DailyEpisodeExample, string? SeriesFolderExample, string? SeasonFolderExample, string? SpecialsFolderExample);

public sealed record EpisodeFileResource
{
    public int Id { get; init; }

    public int SeriesId { get; init; }

    public int SeasonNumber { get; init; }

    public required string RelativePath { get; init; }

    public required string Path { get; init; }

    public long Size { get; init; }

    public DateTime DateAdded { get; init; }

    public string? SceneName { get; init; }

    public string? ReleaseGroup { get; init; }

    public List<Language> Languages { get; init; } = [];

    public required QualityModel Quality { get; init; }

    public List<object> CustomFormats { get; init; } = [];

    public int CustomFormatScore { get; init; }

    public bool QualityCutoffNotMet { get; init; }
}

public static class MediaManagementEndpoints
{
    public static void MapMediaManagementEndpoints(this IEndpointRouteBuilder api)
    {
        var roots = api.MapGroup("/rootfolder");
        roots.MapGet("/", async (RootFolderService service, CancellationToken cancellationToken) => (await service.AllAsync(cancellationToken)).Select(RootFolderResource.From));
        roots.MapGet("/{id:int}", async (int id, RootFolderService service, CancellationToken cancellationToken) =>
            await service.GetAsync(id, cancellationToken) is { } info ? Results.Ok(RootFolderResource.From(info)) : Results.NotFound());
        roots.MapPost("/", CreateRootFolder);
        roots.MapDelete("/{id:int}", DeleteRootFolder);

        var mappings = api.MapGroup("/remotepathmapping");
        mappings.MapGet("/", async (GenerralDbContext db) => (await db.RemotePathMappings.AsNoTracking().OrderBy(m => m.Host).ThenBy(m => m.RemotePath).ToListAsync()).Select(RemotePathMappingResource.From));
        mappings.MapGet("/{id:int}", async (int id, GenerralDbContext db) =>
            await db.RemotePathMappings.FindAsync(id) is { } m ? Results.Ok(RemotePathMappingResource.From(m)) : Results.NotFound());
        mappings.MapPost("/", (RemotePathMappingResource resource, GenerralDbContext db) => SaveRemotePathMapping(null, resource, db));
        mappings.MapPut("/{id:int}", (int id, RemotePathMappingResource resource, GenerralDbContext db) => SaveRemotePathMapping(id, resource, db));
        mappings.MapDelete("/{id:int}", async (int id, GenerralDbContext db) =>
            await db.RemotePathMappings.Where(m => m.Id == id).ExecuteDeleteAsync() > 0 ? Results.Ok(new { }) : Results.NotFound());

        var config = api.MapGroup("/config");
        config.MapGet("/naming", (NamingConfigService service, CancellationToken cancellationToken) => service.GetAsync(cancellationToken));
        config.MapPut("/naming", SaveNaming);
        config.MapGet("/naming/examples", NamingExamples);
        config.MapGet("/mediamanagement", (MediaManagementConfigService service, CancellationToken cancellationToken) => service.GetAsync(cancellationToken));
        config.MapPut("/mediamanagement", SaveMediaManagement);

        var files = api.MapGroup("/episodefile");
        files.MapGet("/", GetEpisodeFiles);
        files.MapGet("/{id:int}", async (int id, GenerralDbContext db, CancellationToken cancellationToken) =>
            (await ToResourcesAsync(db, db.EpisodeFiles.Where(f => f.Id == id), cancellationToken)).SingleOrDefault() is { } file ? Results.Ok(file) : Results.NotFound());
        files.MapDelete("/{id:int}", DeleteEpisodeFile);
    }

    private static async Task<IResult> CreateRootFolder(RootFolderResource resource, GenerralDbContext db)
    {
        var path = resource.Path?.Trim() is { Length: > 1 } p ? p.TrimEnd('/', '\\') : resource.Path?.Trim();
        if (string.IsNullOrEmpty(path) || !System.IO.Path.IsPathFullyQualified(path))
        {
            return ValidationFailure.BadRequest(new ValidationFailure("path", $"Invalid Path: '{resource.Path}'"));
        }

        if (!Directory.Exists(path))
        {
            return ValidationFailure.BadRequest(new ValidationFailure("path", "Folder does not exist"));
        }

        if (await db.RootFolders.AnyAsync(r => r.Path == path))
        {
            return ValidationFailure.BadRequest(new ValidationFailure("path", "Path is already configured as a root folder"));
        }

        var folder = new RootFolder { Path = path };
        db.RootFolders.Add(folder);
        await db.SaveChangesAsync();
        var seriesPaths = (await db.Series.AsNoTracking().Select(s => s.Path).ToListAsync()).ToHashSet(StringComparer.Ordinal);
        return Results.Created($"rootfolder/{folder.Id}", RootFolderResource.From(RootFolderService.Describe(folder, seriesPaths)));
    }

    private static async Task<IResult> DeleteRootFolder(int id, GenerralDbContext db) =>
        await db.RootFolders.Where(r => r.Id == id).ExecuteDeleteAsync() > 0 ? Results.Ok(new { }) : Results.NotFound();

    private static async Task<IResult> SaveRemotePathMapping(int? id, RemotePathMappingResource resource, GenerralDbContext db)
    {
        var failures = new List<ValidationFailure>();
        if (string.IsNullOrWhiteSpace(resource.Host))
        {
            failures.Add(new ValidationFailure("host", "'Host' must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(resource.RemotePath))
        {
            failures.Add(new ValidationFailure("remotePath", "'Remote Path' must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(resource.LocalPath) || !System.IO.Path.IsPathFullyQualified(resource.LocalPath.Trim()))
        {
            failures.Add(new ValidationFailure("localPath", $"Invalid Path: '{resource.LocalPath}'"));
        }
        else if (!Directory.Exists(resource.LocalPath.Trim()))
        {
            failures.Add(new ValidationFailure("localPath", "Folder does not exist"));
        }

        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        var host = resource.Host!.Trim();
        var remote = RemotePathMapping.WithTrailingSlash(resource.RemotePath!);
        if (await db.RemotePathMappings.AnyAsync(m => m.Host == host && m.RemotePath == remote && m.Id != id))
        {
            return ValidationFailure.BadRequest(new ValidationFailure("remotePath", "RemotePath already mounted."));
        }

        var mapping = id is null ? null : await db.RemotePathMappings.FindAsync(id.Value);
        if (id is not null && mapping is null)
        {
            return Results.NotFound();
        }

        if (mapping is null)
        {
            mapping = new RemotePathMapping { Host = host, RemotePath = remote, LocalPath = "" };
            db.RemotePathMappings.Add(mapping);
        }

        mapping.Host = host;
        mapping.RemotePath = remote;
        mapping.LocalPath = RemotePathMapping.WithTrailingSlash(resource.LocalPath!);
        await db.SaveChangesAsync();
        var result = RemotePathMappingResource.From(mapping);
        return id is null ? Results.Created($"remotepathmapping/{mapping.Id}", result) : Results.Accepted($"remotepathmapping/{mapping.Id}", result);
    }

    private static async Task<IResult> SaveNaming(NamingConfig settings, NamingConfigService service)
    {
        try
        {
            return Results.Accepted("config/naming", await service.SaveAsync(settings));
        }
        catch (ArgumentException e)
        {
            return ValidationFailure.BadRequest(new ValidationFailure(e.Message.Contains("folder", StringComparison.OrdinalIgnoreCase) ? "seasonFolderFormat" : "standardEpisodeFormat", e.Message));
        }
    }

    private static async Task<IResult> SaveMediaManagement(MediaManagementConfig settings, MediaManagementConfigService service)
    {
        try
        {
            return Results.Accepted("config/mediamanagement", await service.SaveAsync(settings));
        }
        catch (ArgumentException e)
        {
            return ValidationFailure.BadRequest(new ValidationFailure("recycleBin", e.Message));
        }
    }

    /// <summary>Sonarr's <c>FileNameSampleService</c> with a sports sample (the settings in the query override the stored ones, like Sonarr's UI preview).</summary>
    private static async Task<NamingExampleResource> NamingExamples(HttpRequest request, NamingConfigService service, CancellationToken cancellationToken)
    {
        var stored = await service.GetAsync(cancellationToken);
        string Q(string name, string current) => request.Query[name].FirstOrDefault() is { } value ? value : current;
        var config = stored with
        {
            RenameEpisodes = bool.TryParse(request.Query["renameEpisodes"], out var rename) ? rename : stored.RenameEpisodes,
            StandardEpisodeFormat = Q("standardEpisodeFormat", stored.StandardEpisodeFormat),
            DailyEpisodeFormat = Q("dailyEpisodeFormat", stored.DailyEpisodeFormat),
            SeriesFolderFormat = Q("seriesFolderFormat", stored.SeriesFolderFormat),
            SeasonFolderFormat = Q("seasonFolderFormat", stored.SeasonFolderFormat),
            SpecialsFolderFormat = Q("specialsFolderFormat", stored.SpecialsFolderFormat),
            MultiEpisodeStyle = Enum.TryParse<MultiEpisodeStyle>(request.Query["multiEpisodeStyle"], out var style) ? style : stored.MultiEpisodeStyle,
            ColonReplacementFormat = Enum.TryParse<ColonReplacementFormat>(request.Query["colonReplacementFormat"], out var colon) ? colon : stored.ColonReplacementFormat,
        };

        var series = new Series { Title = "UFC", SortTitle = "ufc", CleanTitle = "ufc", TitleSlug = "ufc", Path = "/data/media/generral/UFC", RootFolderPath = "/data/media/generral", Year = 1993 };
        var daily = new Series { Title = "Formula 1", SortTitle = "formula 1", CleanTitle = "formula1", TitleSlug = "f1", Path = "/f1", RootFolderPath = "/", SeriesType = SeriesType.Daily };
        Episode Episode(int number, string title) => new() { Id = number, SeasonNumber = 2025, EpisodeNumber = number, Title = title, AirDate = new DateOnly(2025, 4, 12) };
        var file = new EpisodeFile
        {
            RelativePath = "",
            Path = "/downloads/UFC.314.1080p.WEB-DL.h264-GRP.mkv",
            SceneName = "UFC.314.1080p.WEB-DL.h264-GRP",
            ReleaseGroup = "GRP",
            Quality = new QualityModel { Quality = Quality.WEBDL1080p },
        };

        string? Name(NamingInput input) => Safe(() => FileNameBuilder.BuildFileName(input, config, ".mkv"));
        return new NamingExampleResource(
            Name(new NamingInput(series, [Episode(4, "UFC 314: Volkanovski vs. Lopes - Main Card")], file)),
            Name(new NamingInput(series, [Episode(4, "UFC 314 - Early Prelims"), Episode(5, "UFC 314 - Prelims"), Episode(6, "UFC 314 - Main Card")], file)),
            Name(new NamingInput(daily, [Episode(5, "Bahrain Grand Prix - Race")], file)),
            Safe(() => FileNameBuilder.GetSeriesFolder(series, config)),
            Safe(() => FileNameBuilder.GetSeasonFolder(series, 2025, null, config)),
            Safe(() => FileNameBuilder.GetSeasonFolder(series, 0, null, config)));
    }

    private static string? Safe(Func<string> build)
    {
        try
        {
            return build();
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static async Task<IResult> GetEpisodeFiles(GenerralDbContext db, CancellationToken cancellationToken, int? seriesId = null, string? episodeFileIds = null)
    {
        IQueryable<EpisodeFile> query = db.EpisodeFiles;
        if (seriesId is { } series)
        {
            query = query.Where(f => f.SeriesId == series);
        }
        else if (episodeFileIds is not null)
        {
            var ids = episodeFileIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(v => int.TryParse(v, out var id) ? id : 0).ToList();
            query = query.Where(f => ids.Contains(f.Id));
        }
        else
        {
            return ValidationFailure.BadRequest(new ValidationFailure("seriesId", "seriesId or episodeFileIds must be provided"));
        }

        return Results.Ok(await ToResourcesAsync(db, query, cancellationToken));
    }

    private static async Task<IResult> DeleteEpisodeFile(int id, GenerralDbContext db, EpisodeImportService importer, MediaManagementConfigService config, CancellationToken cancellationToken)
    {
        if (await db.EpisodeFiles.SingleOrDefaultAsync(f => f.Id == id, cancellationToken) is not { } file ||
            await db.Series.AsNoTracking().SingleOrDefaultAsync(s => s.Id == file.SeriesId, cancellationToken) is not { } series)
        {
            return Results.NotFound();
        }

        try
        {
            await importer.DeleteAsync(file, series, await config.GetAsync(cancellationToken), "Manual", cancellationToken);
        }
        catch (IOException e)
        {
            return Results.Conflict(new { message = e.Message });
        }

        return Results.Ok(new { });
    }

    internal static async Task<List<EpisodeFileResource>> ToResourcesAsync(GenerralDbContext db, IQueryable<EpisodeFile> query, CancellationToken cancellationToken)
    {
        var files = await query.AsNoTracking().OrderBy(f => f.SeasonNumber).ThenBy(f => f.RelativePath).ToListAsync(cancellationToken);
        var seriesIds = files.Select(f => f.SeriesId).Distinct().ToList();
        var profileIds = await db.Series.AsNoTracking().Where(s => seriesIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.QualityProfileId, cancellationToken);
        var profiles = await db.QualityProfiles.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);
        return files.Select(f => new EpisodeFileResource
        {
            Id = f.Id,
            SeriesId = f.SeriesId,
            SeasonNumber = f.SeasonNumber,
            RelativePath = f.RelativePath,
            Path = f.Path,
            Size = f.Size,
            DateAdded = f.DateAdded,
            SceneName = f.SceneName,
            ReleaseGroup = f.ReleaseGroup,
            Languages = f.Languages,
            Quality = f.Quality,
            QualityCutoffNotMet = profiles.TryGetValue(profileIds.GetValueOrDefault(f.SeriesId), out var profile) && UpgradableSpecification.QualityCutoffNotMet(profile, f.Quality),
        }).ToList();
    }
}
