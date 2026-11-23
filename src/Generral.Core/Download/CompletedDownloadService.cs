// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Download/CompletedDownloadService.cs and DownloadEventHub.cs (remove completed)
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.Download.TrackedDownloads;
using Generral.Core.History;
using Generral.Core.MediaFiles;
using Generral.Core.RemotePathMappings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Download;

/// <summary>Imports completed tracked downloads (spec 08 § Completed download handling); matching comes from the grab, not from parsing.</summary>
public sealed class CompletedDownloadService(
    GenerralDbContext db,
    EpisodeImportService importer,
    MediaManagementConfigService mediaManagement,
    DownloadClientProvider provider,
    ILogger<CompletedDownloadService> logger)
{
    public const string PartialImportMessage = "One or more episodes expected in this release were not imported or missing from the release";

    public async Task ImportAsync(DownloadClient client, TrackedDownload tracked, CancellationToken cancellationToken = default)
    {
        var item = tracked.Item!;
        if (string.IsNullOrWhiteSpace(item.OutputPath))
        {
            Block(tracked, "Download doesn't contain intermediate path, Skipping.");
            return;
        }

        var outputPath = await LocalOutputPathAsync(client, item.OutputPath, cancellationToken);
        if (!Path.IsPathFullyQualified(outputPath) || (!Directory.Exists(outputPath) && !File.Exists(outputPath)))
        {
            Block(tracked, $"Download client {client.Name} reports files in {outputPath} but this path doesn't exist in Generral. You may need a Remote Path Mapping.");
            return;
        }

        tracked.State = TrackedDownloadState.Importing;
        var config = await mediaManagement.GetAsync(cancellationToken);
        var mode = item.CanMoveFiles ? TransferMode.Move : config.CopyUsingHardlinks ? TransferMode.HardLinkOrCopy : TransferMode.Copy;
        var sourceFolder = Directory.Exists(outputPath) ? outputPath : Path.GetDirectoryName(outputPath);
        var results = new List<ImportResult>();
        var files = await importer.AssignAsync(outputPath, tracked.EpisodeIds, cancellationToken);
        var ignored = files.Where(f => f.Ignored).Select(f => Path.GetFileName(f.Path)).ToList();
        if (ignored.Count > 0)
        {
            logger.LogInformation("{Title}: ignoring {Files}, another file was picked for the episode", tracked.Title, string.Join(", ", ignored));
        }

        foreach (var file in files.Where(f => !f.Ignored))
        {
            if (file.EpisodeIds.Count == 0)
            {
                results.Add(new ImportResult(file.Path, [], null, [file.Rejection ?? "No episode matched"]));
                continue;
            }

            results.Add(await importer.ImportAsync(new ImportItem
            {
                Path = file.Path,
                SeriesId = tracked.SeriesId!.Value,
                EpisodeIds = file.EpisodeIds,
                Quality = tracked.Quality,
                SceneName = tracked.Title,
                DownloadId = tracked.DownloadId,
                SourceFolder = sourceFolder,
                DownloadClient = client.Implementation,
                DownloadClientName = client.Name,
                IgnoredFiles = ignored,
            }, mode, cancellationToken));
        }

        if (await AllImportedAsync(tracked, results, cancellationToken))
        {
            tracked.State = TrackedDownloadState.Imported;
            tracked.Status = TrackedDownloadStatus.Ok;
            tracked.StatusMessages = [];
            await MarkImportedAsync(client, tracked, cancellationToken);
            return;
        }

        if (results.Count == 0)
        {
            Block(tracked, $"No files found are eligible for import in {outputPath}");
            return;
        }

        tracked.State = TrackedDownloadState.ImportBlocked;
        tracked.Status = TrackedDownloadStatus.Warning;
        tracked.StatusMessages = results.Count == 1 && !results[0].Imported
            ? [new TrackedDownloadStatusMessage(Path.GetFileName(results[0].Path), results[0].Errors)]
            :
            [
                new TrackedDownloadStatusMessage(PartialImportMessage, []),
                .. results.Where(r => !r.Imported).OrderBy(r => r.Path, StringComparer.Ordinal).Select(r => new TrackedDownloadStatusMessage(Path.GetFileName(r.Path), r.Errors)),
            ];
        logger.LogWarning("Couldn't import {Title}: {Messages}", tracked.Title, string.Join("; ", tracked.StatusMessages.SelectMany(m => m.Messages)));
    }

    /// <summary>The download client's path as Generral sees it, through the remote path mappings of the client's host.</summary>
    public async Task<string> LocalOutputPathAsync(DownloadClient client, string outputPath, CancellationToken cancellationToken = default)
    {
        var host = client.Settings["host"]?.GetValue<string>() ?? "";
        var mappings = await db.RemotePathMappings.AsNoTracking().ToListAsync(cancellationToken);
        return RemotePathMappingService.RemapRemoteToLocal(mappings, host, outputPath);
    }

    /// <summary>Removes an imported download from the client once it reached its seeding goals, when the client removes completed downloads.</summary>
    public async Task RemoveIfSeededAsync(DownloadClient client, TrackedDownload tracked, CancellationToken cancellationToken = default)
    {
        if (!client.RemoveCompletedDownloads || tracked.Item?.CanBeRemoved != true || provider.Find(client.Implementation) is not { } implementation)
        {
            return;
        }

        try
        {
            await implementation.RemoveItemAsync(client, tracked.DownloadId, deleteData: true, cancellationToken);
            logger.LogInformation("Removed {Title} from {Client}: imported and seeding goals reached", tracked.Title, client.Name);
        }
        catch (DownloadClientException e)
        {
            logger.LogWarning("Couldn't remove {Title} from {Client}: {Message}", tracked.Title, client.Name, e.Message);
        }
    }

    private async Task<bool> AllImportedAsync(TrackedDownload tracked, List<ImportResult> results, CancellationToken cancellationToken)
    {
        var expected = tracked.EpisodeIds.ToHashSet();
        if (results.Where(r => r.Imported).SelectMany(r => r.EpisodeIds).Distinct().Count() >= Math.Max(1, expected.Count))
        {
            return true;
        }

        var imported = await db.History.AsNoTracking()
            .Where(h => h.DownloadId == tracked.DownloadId && h.EventType == EpisodeHistoryEventType.DownloadFolderImported)
            .Select(h => h.EpisodeId).Distinct().ToListAsync(cancellationToken);
        return expected.Count > 0 && expected.IsSubsetOf(imported);
    }

    private async Task MarkImportedAsync(DownloadClient client, TrackedDownload tracked, CancellationToken cancellationToken)
    {
        if (provider.Find(client.Implementation) is not { } implementation)
        {
            return;
        }

        try
        {
            await implementation.MarkItemAsImportedAsync(client, tracked.Item!, cancellationToken);
        }
        catch (DownloadClientException e)
        {
            logger.LogWarning("Couldn't mark {Title} as imported in {Client}: {Message}", tracked.Title, client.Name, e.Message);
        }

        await RemoveIfSeededAsync(client, tracked, cancellationToken);
    }

    private static void Block(TrackedDownload tracked, string message)
    {
        tracked.State = TrackedDownloadState.ImportBlocked;
        tracked.Warn(message);
    }
}
