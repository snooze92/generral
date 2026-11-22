// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Extras/Metadata/MetadataService.cs
using System.Security.Cryptography;
using System.Text;
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.Sources.Http;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Extras.Metadata;

/// <summary>
/// Brings the metadata files of a series in line with its current state: writes what's missing or changed, deletes what no longer
/// belongs (files of deleted episode files, disabled consumers). Called after an import, a file deletion and a mapping apply.
/// </summary>
public sealed class MetadataService(
    GenerralDbContext db,
    IEnumerable<IMetadataConsumer> consumers,
    IUiSettingsService uiSettings,
    IHttpClientFactory clients,
    TimeProvider time,
    ILogger<MetadataService> logger)
{
    private readonly Dictionary<string, IMetadataConsumer> _consumers = consumers.ToDictionary(c => c.Implementation, StringComparer.OrdinalIgnoreCase);

    public IMetadataConsumer? Find(string? implementation) =>
        implementation is not null && _consumers.TryGetValue(implementation, out var consumer) ? consumer : null;

    /// <returns>The number of files written or deleted.</returns>
    public async Task<int> SyncSeriesAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        var series = await db.Series.AsNoTracking().SingleOrDefaultAsync(s => s.Id == seriesId, cancellationToken);
        var existing = await db.MetadataFiles.Where(f => f.SeriesId == seriesId).ToListAsync(cancellationToken);
        if (series is null)
        {
            return 0;
        }

        var changes = 0;

        var definitions = await db.Metadata.AsNoTracking().Where(m => m.Enable).ToListAsync(cancellationToken);
        var wanted = new List<(MetadataFile File, string? Contents, string? Url)>();
        if (definitions.Count > 0 && Directory.Exists(series.Path))
        {
            var now = time.GetUtcNow().UtcDateTime;
            var spoilerFree = uiSettings.Get().SpoilerFreeMode;
            var seasons = await db.Seasons.AsNoTracking().Where(s => s.SeriesId == seriesId).ToListAsync(cancellationToken);
            var episodes = (await db.Episodes.AsNoTracking().Where(e => e.SeriesId == seriesId).ToListAsync(cancellationToken))
                .Select(e => SpoilerFilter.Apply(e, spoilerFree, now))
                .ToList();
            var files = await db.EpisodeFiles.AsNoTracking().Where(f => f.SeriesId == seriesId).ToListAsync(cancellationToken);
            var premiered = episodes.Where(e => e.SeasonNumber > 0).Select(e => e.AirDate ?? (e.AirDateUtc is { } utc ? DateOnly.FromDateTime(utc) : null)).Min();
            var metadataSeries = new MetadataSeries(series, seasons, premiered);

            foreach (var definition in definitions)
            {
                if (Find(definition.Implementation) is not { } consumer)
                {
                    continue;
                }

                MetadataFile Track(MetadataType type, string relativePath, int? seasonNumber = null, int? episodeFileId = null) => new()
                {
                    SeriesId = seriesId,
                    Consumer = consumer.Implementation,
                    Type = type,
                    RelativePath = relativePath,
                    SeasonNumber = seasonNumber,
                    EpisodeFileId = episodeFileId,
                };

                if (consumer.SeriesMetadata(metadataSeries, definition.Settings) is { } nfo)
                {
                    wanted.Add((Track(MetadataType.SeriesMetadata, nfo.RelativePath), nfo.Contents, null));
                }

                wanted.AddRange(consumer.SeriesImages(series, definition.Settings).Select(i => (Track(MetadataType.SeriesImage, i.RelativePath), (string?)null, (string?)i.Url)));

                foreach (var season in seasons)
                {
                    var folder = files.Where(f => f.SeasonNumber == season.SeasonNumber).Select(f => Path.GetDirectoryName(f.RelativePath) ?? "").FirstOrDefault();
                    if (folder is not null)
                    {
                        wanted.AddRange(consumer.SeasonImages(season, folder, definition.Settings)
                            .Select(i => (Track(MetadataType.SeasonImage, i.RelativePath, season.SeasonNumber), (string?)null, (string?)i.Url)));
                    }
                }

                foreach (var file in files)
                {
                    var fileEpisodes = episodes.Where(e => e.EpisodeFileId == file.Id).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
                    if (consumer.EpisodeMetadata(file, fileEpisodes, definition.Settings) is { } episodeNfo)
                    {
                        wanted.Add((Track(MetadataType.EpisodeMetadata, episodeNfo.RelativePath, file.SeasonNumber, file.Id), episodeNfo.Contents, null));
                    }

                    wanted.AddRange(consumer.EpisodeImages(file, fileEpisodes, definition.Settings)
                        .Select(i => (Track(MetadataType.EpisodeImage, i.RelativePath, file.SeasonNumber, file.Id), (string?)null, (string?)i.Url)));
                }
            }
        }

        var wantedPaths = wanted.Select(w => w.File.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in existing.Where(f => !wantedPaths.Contains(f.RelativePath)))
        {
            Delete(series.Path, stale.RelativePath);
            db.MetadataFiles.Remove(stale);
            changes++;
        }

        foreach (var (file, contents, url) in wanted.DistinctBy(w => w.File.RelativePath))
        {
            var tracked = existing.FirstOrDefault(f => f.RelativePath == file.RelativePath);
            var hash = contents is not null ? Hash(contents) : url;
            var path = Path.Combine(series.Path, file.RelativePath);
            if (tracked is not null && tracked.Hash == hash && File.Exists(path))
            {
                continue;
            }

            if (!await WriteAsync(path, contents, url, cancellationToken))
            {
                continue;
            }

            changes++;
            var now = time.GetUtcNow().UtcDateTime;
            if (tracked is null)
            {
                file.Hash = hash;
                file.Added = now;
                file.LastUpdated = now;
                db.MetadataFiles.Add(file);
            }
            else
            {
                tracked.Hash = hash;
                tracked.EpisodeFileId = file.EpisodeFileId;
                tracked.LastUpdated = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return changes;
    }

    private async Task<bool> WriteAsync(string path, string? contents, string? url, CancellationToken cancellationToken)
    {
        try
        {
            if (contents is not null)
            {
                await File.WriteAllTextAsync(path, contents, new UTF8Encoding(false), cancellationToken);
                logger.LogDebug("Wrote {Path}", path);
                return true;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                return false;
            }

            using var response = await clients.CreateClient(SourceFetcherFactory.ClientName).GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            logger.LogDebug("Downloaded {Url} to {Path}", uri, path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Couldn't write {Path}: {Error}", path, e.Message);
            return false;
        }
    }

    private void Delete(string seriesPath, string relativePath)
    {
        var path = Path.Combine(seriesPath, relativePath);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Couldn't delete {Path}: {Error}", path, e.Message);
        }
    }

    private static string Hash(string contents) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contents)));
}
