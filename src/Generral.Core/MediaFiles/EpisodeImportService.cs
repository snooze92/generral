// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/MediaFiles/DownloadedEpisodesImportService.cs, DiskScanService.cs (exclusions),
// EpisodeImport/ImportApprovedEpisodes.cs, EpisodeImport/Specifications/ (FreeSpace, Upgrade, NotSample, NotUnpacking, AlreadyImported),
// EpisodeFileMovingService.cs, UpgradeMediaFileService.cs and src/NzbDrone.Core/Extras/ExistingExtraFileService.cs (subtitles only)
using System.Text.RegularExpressions;
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.DecisionEngine;
using Generral.Core.Extras.Metadata;
using Generral.Core.History;
using Generral.Core.Mappings;
using Generral.Core.Notifications;
using Generral.Core.Organizer;
using Generral.Core.Profiles;
using Generral.Core.Qualities;
using Generral.Core.RootFolders;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Generral.Core.MediaFiles;

/// <summary>One file to import for given episodes (assigned automatically from the grab, or by the user in manual import).</summary>
public sealed record ImportItem
{
    public required string Path { get; init; }

    public required int SeriesId { get; init; }

    public required IReadOnlyList<int> EpisodeIds { get; init; }

    public required QualityModel Quality { get; init; }

    /// <summary>The grabbed release title, stored as the file's scene name.</summary>
    public string? SceneName { get; init; }

    public string? ReleaseGroup { get; init; }

    public string? DownloadId { get; init; }

    /// <summary>Folder the file was found in, for <c>{Original Filename}</c> and history.</summary>
    public string? SourceFolder { get; init; }

    public string? DownloadClient { get; init; }

    public string? DownloadClientName { get; init; }

    /// <summary>Other video files of the release that were not imported (recorded in the history).</summary>
    public IReadOnlyList<string> IgnoredFiles { get; init; } = [];
}

public sealed record ImportResult(string Path, IReadOnlyList<int> EpisodeIds, EpisodeFile? File, IReadOnlyList<string> Errors)
{
    public bool Imported => File is not null;
}

/// <summary>A video file found in a download folder, with the episodes it was assigned to (empty when none could be).</summary>
public sealed record LocalEpisodeFile(string Path, long Size, IReadOnlyList<int> EpisodeIds, string? Rejection)
{
    /// <summary>Another file of the release was picked for its single episode; this one is left alone.</summary>
    public bool Ignored { get; init; }
}

public sealed partial class EpisodeImportService(
    GenerralDbContext db,
    NamingConfigService naming,
    MediaManagementConfigService mediaManagement,
    QualityDefinitionService qualityDefinitions,
    DiskTransferService transfer,
    RecycleBinProvider recycleBin,
    MetadataService metadata,
    NotificationService notifications,
    TimeProvider time,
    ILogger<EpisodeImportService> logger)
{
    /// <summary>Spec 08 § Completed download handling, step 3.</summary>
    public static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>([".mkv", ".mp4", ".avi", ".m4v", ".ts", ".mov", ".wmv", ".webm"], StringComparer.OrdinalIgnoreCase);

    /// <summary>A file smaller than this share of the largest one is a sample.</summary>
    public const double SampleRatio = 0.05;

    /// <summary>Video files of a download (a file or a folder), without samples, extras or hidden folders, largest first.</summary>
    public static List<FileInfo> VideoFiles(string path)
    {
        if (File.Exists(path))
        {
            return VideoExtensions.Contains(System.IO.Path.GetExtension(path)) ? [new FileInfo(path)] : [];
        }

        if (!Directory.Exists(path))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(f => VideoExtensions.Contains(System.IO.Path.GetExtension(f)))
            .Where(f => !ExcludedSubFoldersRegex().IsMatch(System.IO.Path.GetRelativePath(path, f)) && !ExcludedExtrasSubFolderRegex().IsMatch(System.IO.Path.GetRelativePath(path, f)))
            .Where(f => !ExcludedExtraFilesRegex().IsMatch(System.IO.Path.GetFileName(f)) && !ExcludedFilesRegex().IsMatch(System.IO.Path.GetFileName(f)))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.Length)
            .ToList();
        var largest = files.FirstOrDefault()?.Length ?? 0;
        return files.Where(f => !SampleNameRegex().IsMatch(System.IO.Path.GetFileNameWithoutExtension(f.Name)) && f.Length >= largest * SampleRatio).ToList();
    }

    /// <summary>
    /// Spec 08 step 4: one episode + one file, or one file for several episodes (multi-episode file), or each file matched against the episodes'
    /// rules (<c>must</c>/<c>mustNot</c>/<c>any</c>, event and part); anything else is left for manual import.
    /// </summary>
    public async Task<List<LocalEpisodeFile>> AssignAsync(string path, IReadOnlyList<int> episodeIds, CancellationToken cancellationToken = default)
    {
        var files = VideoFiles(path);
        if (files.Count == 0 || episodeIds.Count == 0)
        {
            return files.Select(f => new LocalEpisodeFile(f.FullName, f.Length, [], null)).ToList();
        }

        if (files.Count == 1)
        {
            return [new LocalEpisodeFile(files[0].FullName, files[0].Length, episodeIds, null)];
        }

        var candidates = await ReleaseSearchCandidatesAsync(episodeIds, cancellationToken);
        if (episodeIds.Count == 1)
        {
            if (candidates.Count == 1 && PickFile(files, candidates[0]) is { } picked)
            {
                return files.Select(f => f == picked
                    ? new LocalEpisodeFile(f.FullName, f.Length, episodeIds, null)
                    : new LocalEpisodeFile(f.FullName, f.Length, [], $"Ignored: {picked.Name} was imported for {candidates[0].Label}") { Ignored = true }).ToList();
            }

            return files.Select(f => new LocalEpisodeFile(f.FullName, f.Length, [], "Several video files for a single episode: manual import required")).ToList();
        }

        var matches = files.Select(f => (File: f, Episodes: candidates.Where(c => c.HasRules && ReleaseTitleMatcher.Check(c.Terms, f.Name) is null).ToList())).ToList();
        var claimed = matches.Where(m => m.Episodes.Count == 1).GroupBy(m => m.Episodes[0].Episode.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        return matches.Select(m => m.Episodes switch
        {
            [] => new LocalEpisodeFile(m.File.FullName, m.File.Length, [], "Matches none of the release's episodes: manual import required"),
            [var one] when claimed.Contains(one.Episode.Id) => new LocalEpisodeFile(m.File.FullName, m.File.Length, [], $"Several files match {one.Label}: manual import required"),
            [var one] => new LocalEpisodeFile(m.File.FullName, m.File.Length, [one.Episode.Id], null),
            var several => new LocalEpisodeFile(m.File.FullName, m.File.Length, [], $"Ambiguous: matches {string.Join(", ", several.Select(c => c.Label))}: manual import required"),
        }).ToList();
    }

    /// <summary>
    /// Manual import of a folder for a series: each file gets the one active episode whose rules match its name (spec 08 § Manual import);
    /// files matching none or several are left for the user to assign.
    /// </summary>
    public async Task<List<LocalEpisodeFile>> SuggestAsync(string path, int seriesId, CancellationToken cancellationToken = default)
    {
        var files = VideoFiles(path);
        var episodeIds = await db.Episodes.AsNoTracking().Where(e => e.SeriesId == seriesId && e.Status == EpisodeStatus.Active).Select(e => e.Id).ToListAsync(cancellationToken);
        var candidates = (await ReleaseSearchCandidatesAsync(episodeIds, cancellationToken)).Where(c => c.HasRules).ToList();
        return files.Select(f => candidates.Where(c => ReleaseTitleMatcher.Check(c.Terms, f.Name) is null).Take(2).ToList() switch
        {
            [var one] => new LocalEpisodeFile(f.FullName, f.Length, [one.Episode.Id], null),
            [] => new LocalEpisodeFile(f.FullName, f.Length, [], "Matches none of the series' episodes: choose the episode"),
            _ => new LocalEpisodeFile(f.FullName, f.Length, [], "Matches several episodes: choose the episode"),
        }).ToList();
    }

    /// <summary>
    /// The file of a multi-file release (e.g. <c>Main Card.mkv</c> + <c>Prelims.mkv</c>) for its single episode: the only one matching the episode's
    /// rules, else the only one not excluded by its <c>mustNot</c> rules, else the only one named like the episode title, else the only "main card", else the
    /// largest when at least twice the size of the next. Null when still ambiguous.
    /// </summary>
    internal static FileInfo? PickFile(IReadOnlyList<FileInfo> files, SearchCandidate candidate)
    {
        static FileInfo? Single(IEnumerable<FileInfo> matches) => matches.Take(2).ToList() is [var one] ? one : null;

        if (candidate.HasRules && Single(files.Where(f => ReleaseTitleMatcher.Check(candidate.Terms, f.Name) is null)) is { } matching)
        {
            return matching;
        }

        var allowed = files.Where(f => ReleaseTitleMatcher.Check(candidate.Terms with { Must = [], Any = [] }, f.Name) is null).ToList();
        if (allowed.Count == 1)
        {
            return allowed[0];
        }

        static string Words(string text) => $" {string.Join(' ', WordsRegex().Matches(text.ToLowerInvariant()).Select(m => m.Value))} ";
        var title = Words(candidate.Episode.Title);
        foreach (var named in new Func<string, bool>[]
                 {
                     stem => stem.Trim().Length > 0 && (title.Contains(stem, StringComparison.Ordinal) || stem.Contains(title, StringComparison.Ordinal)),
                     stem => stem.Contains(" main card ", StringComparison.Ordinal),
                 })
        {
            if (Single(allowed.Where(f => named(Words(System.IO.Path.GetFileNameWithoutExtension(f.Name))))) is { } picked)
            {
                return picked;
            }
        }

        var bySize = allowed.OrderByDescending(f => f.Length).ToList();
        return bySize.Count >= 2 && bySize[0].Length >= 2 * bySize[1].Length ? bySize[0] : null;
    }

    /// <summary>Checks the specifications, then transfers, renames and records the file (spec 08 steps 5–10).</summary>
    public async Task<ImportResult> ImportAsync(ImportItem item, TransferMode mode, CancellationToken cancellationToken = default)
    {
        if (item.Quality.Quality == Quality.Unknown)
        {
            // A bare release title ("UFC 334") has no quality; the file name may.
            item = item with { Quality = QualityParser.ParseQuality(System.IO.Path.GetFileName(item.Path)) };
        }

        var series = await db.Series.SingleOrDefaultAsync(s => s.Id == item.SeriesId, cancellationToken);
        var episodes = await db.Episodes.Where(e => item.EpisodeIds.Contains(e.Id) && e.SeriesId == item.SeriesId)
            .OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToListAsync(cancellationToken);
        if (series is null || episodes.Count == 0 || episodes.Count != item.EpisodeIds.Distinct().Count())
        {
            return Reject(item, "Unknown series or episodes");
        }

        if (episodes.Select(e => e.SeasonNumber).Distinct().Count() > 1)
        {
            return Reject(item, "Episodes of several seasons can't share one file");
        }

        var config = await mediaManagement.GetAsync(cancellationToken);
        var namingConfig = await naming.GetAsync(cancellationToken);
        var profile = await db.QualityProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == series.QualityProfileId, cancellationToken);
        var existingIds = episodes.Select(e => e.EpisodeFileId).OfType<int>().Distinct().ToList();
        var existing = await db.EpisodeFiles.Where(f => existingIds.Contains(f.Id)).ToListAsync(cancellationToken);
        var size = new FileInfo(item.Path).Length;

        if (await RejectionAsync(item, series, existing, profile, size, config, cancellationToken) is { } rejection)
        {
            return Reject(item, rejection);
        }

        var extension = System.IO.Path.GetExtension(item.Path);
        var file = new EpisodeFile
        {
            SeriesId = series.Id,
            SeasonNumber = episodes[0].SeasonNumber,
            RelativePath = "",
            Path = "",
            Size = size,
            DateAdded = time.GetUtcNow().UtcDateTime,
            SceneName = item.SceneName,
            ReleaseGroup = item.ReleaseGroup ?? ReleaseGroupParser.Parse(item.SceneName ?? System.IO.Path.GetFileName(item.Path)),
            Quality = item.Quality,
            Languages = DownloadDecisionMaker.ParseLanguages(item.SceneName ?? System.IO.Path.GetFileName(item.Path)),
            OriginalFilePath = item.SourceFolder is null ? System.IO.Path.GetFileName(item.Path) : System.IO.Path.GetRelativePath(item.SourceFolder, item.Path),
        };
        var input = await NamingInputAsync(series, episodes, file, cancellationToken);
        var destination = FileNameBuilder.BuildFilePath(input, namingConfig, extension);

        try
        {
            foreach (var old in existing)
            {
                await DeleteAsync(old, series, config, UpgradeReason, cancellationToken);
            }

            var parent = System.IO.Path.GetDirectoryName(destination)!;
            var created = !Directory.Exists(parent);
            var used = transfer.TransferFile(item.Path, destination, mode);
            SetPermissions(config, created ? parent : null, destination);
            if (config.ImportExtraFiles)
            {
                ImportSubtitles(config, item.Path, destination, mode);
            }

            logger.LogInformation("Imported {Source} to {Destination} ({Mode})", item.Path, destination, used);
        }
        catch (IOException e)
        {
            logger.LogWarning(e, "Couldn't import {Path}", item.Path);
            return Reject(item, $"Couldn't import the file: {e.Message}");
        }

        file.Path = destination;
        file.RelativePath = System.IO.Path.GetRelativePath(series.Path, destination);
        db.EpisodeFiles.Add(file);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var episode in episodes)
        {
            episode.EpisodeFileId = file.Id;
            episode.NumberLocked = true;
        }

        RecordImported(item, series, episodes, file);
        await db.SaveChangesAsync(cancellationToken);
        await metadata.SyncSeriesAsync(series.Id, cancellationToken);
        await notifications.NotifyAsync(existing.Count > 0 ? NotificationTrigger.Upgrade : NotificationTrigger.Download, new DownloadMessage
        {
            Series = series,
            Episodes = episodes,
            File = file,
            OldFiles = existing,
            SourcePath = item.Path,
            DownloadClientName = item.DownloadClientName,
            DownloadClientType = item.DownloadClient,
            DownloadId = item.DownloadId,
            Release = await GrabbedReleaseAsync(item.DownloadId, cancellationToken),
        }, cancellationToken);
        return new ImportResult(item.Path, item.EpisodeIds, file, []);
    }

    /// <summary>The <see cref="DeleteAsync"/> reason when a better file replaces this one.</summary>
    public const string UpgradeReason = "Upgrade";

    /// <summary>Deletes (or recycles) the file on disk and its record; the episodes lose their file.</summary>
    /// <exception cref="IOException">The file couldn't be recycled.</exception>
    public async Task DeleteAsync(EpisodeFile file, Series series, MediaManagementConfig config, string reason, CancellationToken cancellationToken = default)
    {
        if (File.Exists(file.Path))
        {
            var subfolder = System.IO.Path.GetRelativePath(System.IO.Path.GetDirectoryName(series.Path) ?? series.Path, System.IO.Path.GetDirectoryName(file.Path)!);
            recycleBin.DeleteFile(config, file.Path, subfolder.StartsWith("..", StringComparison.Ordinal) ? "" : subfolder);
        }

        var episodes = await db.Episodes.Where(e => e.EpisodeFileId == file.Id).ToListAsync(cancellationToken);
        foreach (var episode in episodes)
        {
            episode.EpisodeFileId = null;
            db.History.Add(new EpisodeHistory
            {
                EpisodeId = episode.Id,
                SeriesId = file.SeriesId,
                SourceTitle = file.SceneName ?? System.IO.Path.GetFileNameWithoutExtension(file.Path),
                Quality = file.Quality,
                Date = time.GetUtcNow().UtcDateTime,
                EventType = EpisodeHistoryEventType.EpisodeFileDeleted,
                Data = new Dictionary<string, string?> { ["reason"] = reason, ["releaseGroup"] = file.ReleaseGroup, ["size"] = file.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            });
        }

        db.EpisodeFiles.Remove(file);
        await db.SaveChangesAsync(cancellationToken);
        var upgrade = reason == UpgradeReason;
        if (!upgrade)
        {
            await metadata.SyncSeriesAsync(series.Id, cancellationToken);
            DeleteEmptyFolders(config, series, file.Path);
        }

        await notifications.NotifyAsync(
            upgrade ? NotificationTrigger.EpisodeFileDeleteForUpgrade : NotificationTrigger.EpisodeFileDelete,
            new EpisodeFileDeleteMessage { Series = series, Episodes = episodes, File = file, Reason = reason },
            cancellationToken);
    }

    private async Task<GrabbedRelease?> GrabbedReleaseAsync(string? downloadId, CancellationToken cancellationToken)
    {
        if (downloadId is null)
        {
            return null;
        }

        var grab = await db.History.AsNoTracking().Where(h => h.DownloadId == downloadId && h.EventType == EpisodeHistoryEventType.Grabbed)
            .OrderByDescending(h => h.Id).FirstOrDefaultAsync(cancellationToken);
        return grab is null
            ? null
            : new GrabbedRelease(
                grab.SourceTitle,
                grab.Data.GetValueOrDefault(EpisodeHistory.Indexer),
                long.TryParse(grab.Data.GetValueOrDefault(EpisodeHistory.Size), System.Globalization.CultureInfo.InvariantCulture, out var size) ? size : null);
    }

    public async Task<NamingInput> NamingInputAsync(Series series, IReadOnlyList<Episode> episodes, EpisodeFile file, CancellationToken cancellationToken = default)
    {
        var ids = episodes.Select(e => e.Id).ToList();
        var links = await db.EpisodeLinks.AsNoTracking().Include(l => l.Event).Include(l => l.Mapping)
            .Where(l => ids.Contains(l.EpisodeId)).OrderBy(l => l.Mapping!.Priority).ThenBy(l => l.Id).ToListAsync(cancellationToken);
        var info = new Dictionary<int, EpisodeNamingInfo>();
        foreach (var link in links.Where(l => !info.ContainsKey(l.EpisodeId)))
        {
            var partKey = link.PartKey == EpisodeLink.NoPart ? null : link.PartKey;
            info[link.EpisodeId] = new EpisodeNamingInfo(link.Event?.Key, partKey, partKey is null ? null : PartTitle(link.Mapping, partKey), link.Event?.Fields);
        }

        var season = episodes.Min(e => e.SeasonNumber);
        var seasonTitle = await db.Seasons.AsNoTracking().Where(s => s.SeriesId == series.Id && s.SeasonNumber == season).Select(s => s.Title).FirstOrDefaultAsync(cancellationToken);
        var titles = (await qualityDefinitions.AllAsync()).ToDictionary(d => d.Quality.Id, d => d.Title);
        return new NamingInput(series, episodes, file) { Info = info, QualityTitles = titles, SeasonTitle = seasonTitle };
    }

    private async Task<string?> RejectionAsync(ImportItem item, Series series, List<EpisodeFile> existing, QualityProfile? profile, long size, MediaManagementConfig config, CancellationToken cancellationToken)
    {
        if (UnpackingRegex().IsMatch(item.Path))
        {
            return "File is still being unpacked";
        }

        if (item.DownloadId is { } downloadId)
        {
            var imported = await db.History.AsNoTracking()
                .Where(h => h.DownloadId == downloadId && h.EventType == EpisodeHistoryEventType.DownloadFolderImported && item.EpisodeIds.Contains(h.EpisodeId))
                .Select(h => h.Date).ToListAsync(cancellationToken);
            if (imported.Count >= item.EpisodeIds.Count && existing.Count > 0)
            {
                return $"Episode file already imported at {imported.Max():u}";
            }
        }

        if (profile is not null && existing.Select(f => UpgradeRejection(profile, f.Quality, item.Quality)).FirstOrDefault(r => r is not null) is { } notUpgrade)
        {
            return notUpgrade;
        }

        if (!config.SkipFreeSpaceCheckWhenImporting && FreeSpace(series) is { } free && free - size < config.MinimumFreeSpaceWhenImporting * 1024L * 1024L)
        {
            return "Not enough free space";
        }

        return null;
    }

    private static string? UpgradeRejection(QualityProfile profile, QualityModel existing, QualityModel candidate)
    {
        var compare = UpgradableSpecification.Compare(profile, candidate.Quality, existing.Quality);
        if (compare < 0)
        {
            return $"Not an upgrade for existing episode file(s). Existing quality: {existing.Quality}. New Quality {candidate.Quality}.";
        }

        return compare == 0 && candidate.Revision.CompareTo(existing.Revision) < 0 ? "Not a quality revision upgrade for existing episode file(s)" : null;
    }

    /// <summary>Free space of the series folder, or of its nearest existing ancestor.</summary>
    private static long? FreeSpace(Series series)
    {
        var path = series.Path;
        while (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
        {
            path = System.IO.Path.GetDirectoryName(path);
        }

        return string.IsNullOrEmpty(path) ? null : RootFolderService.DiskSpace(path).Free;
    }

    private void RecordImported(ImportItem item, Series series, List<Episode> episodes, EpisodeFile file)
    {
        foreach (var episode in episodes)
        {
            db.History.Add(new EpisodeHistory
            {
                EpisodeId = episode.Id,
                SeriesId = series.Id,
                SourceTitle = file.SceneName ?? System.IO.Path.GetFileNameWithoutExtension(item.Path),
                Quality = file.Quality,
                Date = file.DateAdded,
                EventType = EpisodeHistoryEventType.DownloadFolderImported,
                DownloadId = item.DownloadId,
                Data = new Dictionary<string, string?>
                {
                    ["fileId"] = file.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["droppedPath"] = item.Path,
                    ["importedPath"] = file.Path,
                    [EpisodeHistory.DownloadClient] = item.DownloadClient,
                    [EpisodeHistory.DownloadClientName] = item.DownloadClientName,
                    ["releaseGroup"] = file.ReleaseGroup,
                    [EpisodeHistory.Size] = file.Size.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["ignoredFiles"] = item.IgnoredFiles.Count > 0 ? string.Join(", ", item.IgnoredFiles) : null,
                },
            });
        }
    }

    /// <summary>Subtitles next to the video with the same base name (<c>name.en.srt</c>) follow it as <c>&lt;new name&gt;.en.srt</c>.</summary>
    private void ImportSubtitles(MediaManagementConfig config, string source, string destination, TransferMode mode)
    {
        var extensions = config.ExtraFileExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => "." + e.TrimStart('.')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folder = System.IO.Path.GetDirectoryName(source)!;
        var baseName = System.IO.Path.GetFileNameWithoutExtension(source);
        var destinationBase = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(destination)!, System.IO.Path.GetFileNameWithoutExtension(destination));
        foreach (var extra in Directory.EnumerateFiles(folder, baseName + ".*").Where(f => extensions.Contains(System.IO.Path.GetExtension(f))))
        {
            var suffix = System.IO.Path.GetFileName(extra)[baseName.Length..];
            try
            {
                transfer.TransferFile(extra, destinationBase + suffix, mode == TransferMode.Move ? TransferMode.Move : TransferMode.Copy, overwrite: true);
            }
            catch (IOException e)
            {
                logger.LogWarning("Couldn't import extra file {Path}: {Message}", extra, e.Message);
            }
        }
    }

    private void SetPermissions(MediaManagementConfig config, string? createdFolder, string file)
    {
        if (!config.SetPermissionsLinux || OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var folderMode = (UnixFileMode)Convert.ToInt32(config.ChmodFolder, 8);
            if (createdFolder is not null)
            {
                File.SetUnixFileMode(createdFolder, folderMode);
            }

            // Sonarr derives the file mode from the folder's, without the execute bits.
            File.SetUnixFileMode(file, folderMode & ~(UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute));

            if (config.ChownGroup.Trim() is { Length: > 0 } group)
            {
                if (UnixGroup.Resolve(group) is not { } gid)
                {
                    logger.LogWarning("Unknown chown group {Group}", group);
                    return;
                }

                if (createdFolder is not null)
                {
                    UnixGroup.Chown(createdFolder, gid);
                }

                UnixGroup.Chown(file, gid);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Couldn't set permissions on {Path}: {Message}", file, e.Message);
        }
    }

    /// <summary>
    /// <i>Delete Empty Folders</i>: after a file is removed (and its metadata with it), empty folders between it and the series folder
    /// are deleted. The series folder itself stays, like in Sonarr.
    /// </summary>
    private void DeleteEmptyFolders(MediaManagementConfig config, Series series, string filePath)
    {
        if (!config.DeleteEmptyFolders)
        {
            return;
        }

        var seriesFolder = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(series.Path)) + System.IO.Path.DirectorySeparatorChar;
        for (var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(filePath)); folder is not null; folder = System.IO.Path.GetDirectoryName(folder))
        {
            if (!folder.StartsWith(seriesFolder, StringComparison.Ordinal) || !Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any())
            {
                return;
            }

            try
            {
                Directory.Delete(folder);
                logger.LogInformation("Deleted empty folder {Folder}", folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Couldn't delete empty folder {Folder}: {Message}", folder, e.Message);
                return;
            }
        }
    }

    private async Task<List<SearchCandidate>> ReleaseSearchCandidatesAsync(IReadOnlyList<int> episodeIds, CancellationToken cancellationToken) =>
        (await db.Episodes.AsNoTracking().Include(e => e.Series).Where(e => episodeIds.Contains(e.Id)).ToListAsync(cancellationToken))
        .Select(e => new SearchCandidate(e, SearchTerms.From(e.SearchTerms, e.Series?.SearchSettings)))
        .ToList();

    private static string? PartTitle(Mapping? mapping, string partKey)
    {
        try
        {
            return mapping is null ? null : MappingDefinitionParser.FromYaml(mapping.Definition).Parts?.FirstOrDefault(p => p.Key == partKey)?.Values["title"]?.ToString();
        }
        catch (MappingDefinitionException)
        {
            return null;
        }
    }

    private static ImportResult Reject(ImportItem item, string reason) => new(item.Path, item.EpisodeIds, null, [reason]);

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordsRegex();

    [GeneratedRegex(@"(?:\\|\/|^)(?:extras|extrafanart|behind the scenes|deleted scenes|featurettes|interviews|other|scenes|samples|shorts|trailers|theme[-_. ]music|backdrops)(?:\\|\/)", RegexOptions.IgnoreCase)]
    private static partial Regex ExcludedExtrasSubFolderRegex();

    [GeneratedRegex(@"(?:\\|\/|^)(?:@eadir|\.@__thumb|plex versions|\.[^\\/]+)(?:\\|\/)", RegexOptions.IgnoreCase)]
    private static partial Regex ExcludedSubFoldersRegex();

    [GeneratedRegex(@"(-(trailer|other|behindthescenes|deleted|featurette|interview|scene|short)\.[^.]+$)", RegexOptions.IgnoreCase)]
    private static partial Regex ExcludedExtraFilesRegex();

    [GeneratedRegex(@"^\.(_|unmanic|DS_Store$)|^Thumbs\.db$", RegexOptions.IgnoreCase)]
    private static partial Regex ExcludedFilesRegex();

    [GeneratedRegex(@"(?:^|[-._ ])sample(?:$|[-._ ])", RegexOptions.IgnoreCase)]
    private static partial Regex SampleNameRegex();

    [GeneratedRegex(@"(?:\\|\/)_(?:UNPACK|FAILED)_", RegexOptions.IgnoreCase)]
    private static partial Regex UnpackingRegex();
}
