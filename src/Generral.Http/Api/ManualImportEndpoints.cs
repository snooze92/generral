// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/ManualImport/ManualImportController.cs and ManualImportResource.cs
using Generral.Core.Datastore;
using Generral.Core.Download;
using Generral.Core.Download.TrackedDownloads;
using Generral.Core.Languages;
using Generral.Core.MediaFiles;
using Generral.Core.Qualities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

public sealed record ImportRejectionResource(string Reason, string Type = "permanent");

public sealed record ManualImportResource
{
    public int Id { get; init; }

    public required string Path { get; init; }

    public required string RelativePath { get; init; }

    public required string FolderName { get; init; }

    public required string Name { get; init; }

    public long Size { get; init; }

    public SeriesResource? Series { get; init; }

    public int? SeasonNumber { get; init; }

    public List<EpisodeResource> Episodes { get; init; } = [];

    public string? ReleaseGroup { get; init; }

    public required QualityModel Quality { get; init; }

    public List<Language> Languages { get; init; } = [];

    public string? DownloadId { get; init; }

    public List<ImportRejectionResource> Rejections { get; init; } = [];
}

public static class ManualImportEndpoints
{
    public static void MapManualImportEndpoints(this IEndpointRouteBuilder api) =>
        api.MapGroup("/manualimport").MapGet("/", GetMediaFiles);

    /// <summary>
    /// Lists the video files of <c>folder</c> (or of the tracked download <c>downloadId</c>), with the episodes Generral would assign them
    /// (from the grab); import them with the <c>ManualImport</c> command.
    /// </summary>
    private static async Task<IResult> GetMediaFiles(
        GenerralDbContext db,
        EpisodeImportService importer,
        TrackedDownloadService tracked,
        CompletedDownloadService completed,
        TimeProvider time,
        CancellationToken cancellationToken,
        string? folder = null,
        string? downloadId = null,
        int? seriesId = null)
    {
        var download = downloadId is null ? null : tracked.Queue().FirstOrDefault(t => string.Equals(t.DownloadId, downloadId, StringComparison.OrdinalIgnoreCase));
        var path = folder;
        if (path is null && download?.Item?.OutputPath is { } output && await db.DownloadClients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == download.DownloadClientId, cancellationToken) is { } client)
        {
            path = await completed.LocalOutputPathAsync(client, output, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path))
        {
            return ValidationFailure.BadRequest(new ValidationFailure("folder", "folder must be an absolute path, or downloadId a download in the queue"));
        }

        var series = seriesId ?? download?.SeriesId;
        var files = await importer.AssignAsync(path, series is null ? [] : download?.EpisodeIds ?? [], cancellationToken);
        var root = Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(path)!;
        var seriesResource = series is null || await db.Series.AsNoTracking().SingleOrDefaultAsync(s => s.Id == series, cancellationToken) is not { } s
            ? null
            : await SeriesEndpoints.ToResourceAsync(db, s, time);
        var episodeIds = files.SelectMany(f => f.EpisodeIds).Distinct().ToList();
        var episodes = (await EpisodeEndpoints.ToResourcesAsync(db, await db.Episodes.AsNoTracking().Where(e => episodeIds.Contains(e.Id)).ToListAsync(cancellationToken)))
            .ToDictionary(e => e.Id);

        return Results.Ok(files.Select((f, i) =>
        {
            var name = System.IO.Path.GetFileName(f.Path);
            var quality = download?.Quality is { Quality.Id: > 0 } grabbed ? grabbed : QualityParser.ParseQuality(name);
            var rejections = f.Rejection is null ? (seriesResource is null ? [new ImportRejectionResource("Unknown Series")] : []) : new List<ImportRejectionResource> { new(f.Rejection) };
            var assigned = f.EpisodeIds.Select(id => episodes[id]).ToList();
            return new ManualImportResource
            {
                Id = i + 1,
                Path = f.Path,
                RelativePath = System.IO.Path.GetRelativePath(root, f.Path),
                FolderName = System.IO.Path.GetFileName(root),
                Name = System.IO.Path.GetFileNameWithoutExtension(name),
                Size = f.Size,
                Series = seriesResource,
                SeasonNumber = assigned.FirstOrDefault()?.SeasonNumber,
                Episodes = assigned,
                ReleaseGroup = ReleaseGroupParser.Parse(download?.Title ?? name),
                Quality = quality,
                Languages = Core.DecisionEngine.DownloadDecisionMaker.ParseLanguages(download?.Title ?? name),
                DownloadId = download?.DownloadId,
                Rejections = rejections,
            };
        }).ToList());
    }
}
