// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Indexers/ReleaseController.cs, ReleaseControllerBase.cs and ReleaseResource.cs
using System.Text.Json.Serialization;
using Generral.Core.Datastore;
using Generral.Core.DecisionEngine;
using Generral.Core.Download;
using Generral.Core.Indexers;
using Generral.Core.IndexerSearch;
using Generral.Core.Languages;
using Generral.Core.Profiles;
using Generral.Core.Qualities;
using Generral.Core.Tv;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Generral.Http.Api;

/// <summary>Sonarr's <c>ReleaseResource</c> without scene parsing fields (TVDB/IMDb ids, custom formats).</summary>
public sealed record ReleaseResource
{
    public string Guid { get; init; } = "";

    public QualityModel Quality { get; init; } = new();

    public List<Language> Languages { get; init; } = [];

    public int QualityWeight { get; init; }

    public int Age { get; init; }

    public double AgeHours { get; init; }

    public double AgeMinutes { get; init; }

    public long Size { get; init; }

    public int IndexerId { get; init; }

    public string Indexer { get; init; } = "";

    public string Title { get; init; } = "";

    public bool FullSeason => false;

    public bool SceneSource => false;

    public int? MappedSeriesId { get; init; }

    public int? MappedSeasonNumber { get; init; }

    public int[] MappedEpisodeNumbers { get; init; } = [];

    public int[] MappedAbsoluteEpisodeNumbers { get; init; } = [];

    public List<ReleaseEpisodeResource> MappedEpisodeInfo { get; init; } = [];

    public bool Approved { get; init; }

    public bool TemporarilyRejected => false;

    public bool Rejected { get; init; }

    public List<string> Rejections { get; init; } = [];

    public DateTime PublishDate { get; init; }

    public string? CommentUrl { get; init; }

    public string? DownloadUrl { get; init; }

    public string? InfoUrl { get; init; }

    public bool EpisodeRequested { get; init; }

    public bool DownloadAllowed { get; init; }

    public int ReleaseWeight { get; init; }

    public string? MagnetUrl { get; init; }

    public string? InfoHash { get; init; }

    public int? Seeders { get; init; }

    public int? Leechers { get; init; }

    public DownloadProtocol Protocol { get; init; }

    public int IndexerFlags { get; init; }

    /// <summary>Sent when grabbing: assigns the release to these episodes instead of the matched ones.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<int>? EpisodeIds { get; init; }

    public static ReleaseResource From(DownloadDecision decision, int weight, QualityProfile defaultProfile, DateTime now)
    {
        var remote = decision.RemoteEpisode;
        var release = remote.Release;
        var age = now - release.PublishDate;
        return new ReleaseResource
        {
            Guid = release.Guid,
            Quality = remote.Quality,
            Languages = [.. remote.Languages],
            QualityWeight = (defaultProfile.GetIndex(remote.Quality.Quality.Id).Index * 100) + (remote.Quality.Revision.Real * 10) + remote.Quality.Revision.Version,
            Age = (int)age.TotalDays,
            AgeHours = age.TotalHours,
            AgeMinutes = age.TotalMinutes,
            Size = release.Size,
            IndexerId = release.IndexerId,
            Indexer = release.Indexer,
            Title = release.Title,
            MappedSeriesId = remote.Series?.Id,
            MappedSeasonNumber = remote.Episodes.FirstOrDefault()?.SeasonNumber,
            MappedEpisodeNumbers = remote.Episodes.Select(e => e.EpisodeNumber).ToArray(),
            MappedAbsoluteEpisodeNumbers = remote.Episodes.Select(e => e.AbsoluteEpisodeNumber).OfType<int>().ToArray(),
            MappedEpisodeInfo = remote.Episodes.Select(e => new ReleaseEpisodeResource(e.Id, e.SeasonNumber, e.EpisodeNumber, e.AbsoluteEpisodeNumber, e.Title)).ToList(),
            Approved = decision.Approved,
            Rejected = !decision.Approved,
            Rejections = [.. decision.Rejections],
            PublishDate = release.PublishDate,
            CommentUrl = release.CommentUrl,
            DownloadUrl = release.DownloadUrl,
            InfoUrl = release.InfoUrl,
            EpisodeRequested = remote.EpisodeRequested,
            DownloadAllowed = remote.Episodes.Count > 0,
            ReleaseWeight = weight,
            MagnetUrl = release.MagnetUrl,
            InfoHash = release.InfoHash,
            Seeders = release.Seeders,
            Leechers = release.Peers is { } peers && release.Seeders is { } seeders ? peers - seeders : null,
            Protocol = release.DownloadProtocol,
            IndexerFlags = (int)release.IndexerFlags,
        };
    }
}

public sealed record ReleaseEpisodeResource(int Id, int SeasonNumber, int EpisodeNumber, int? AbsoluteEpisodeNumber, string Title);

public static class ReleaseEndpoints
{
    /// <summary>Like Sonarr: an interactive search result can be grabbed for 30 minutes.</summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public static void MapReleaseEndpoints(this IEndpointRouteBuilder api)
    {
        var releases = api.MapGroup("/release");
        releases.MapGet("/", Search);
        releases.MapPost("/", Grab);
    }

    private static async Task<IResult> Search(int? episodeId, ReleaseSearchService search, IMemoryCache cache, GenerralDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        if (episodeId is not { } id)
        {
            return ValidationFailure.BadRequest(new ValidationFailure("episodeId", "episodeId is required"));
        }

        if (!await db.Episodes.AnyAsync(e => e.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var decisions = await search.InteractiveSearchAsync(id, cancellationToken);
        var definitions = await db.QualityDefinitions.AsNoTracking().ToListAsync(cancellationToken);
        var defaultProfile = QualityProfileService.GetDefaultProfile("", definitions);
        var now = time.GetUtcNow().UtcDateTime;
        return Results.Ok(decisions.Select((decision, weight) =>
        {
            var resource = ReleaseResource.From(decision, weight, defaultProfile, now);
            cache.Set(CacheKey(resource.IndexerId, resource.Guid), decision.RemoteEpisode, CacheDuration);
            return resource;
        }).ToList());
    }

    private static async Task<IResult> Grab(ReleaseResource release, IMemoryCache cache, GenerralDbContext db, IDownloadService downloads, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        if (string.IsNullOrWhiteSpace(release.Guid))
        {
            failures.Add(new ValidationFailure("guid", "'Guid' must not be empty."));
        }

        if (release.IndexerId <= 0)
        {
            failures.Add(new ValidationFailure("indexerId", "'Indexer Id' must be greater than '0'."));
        }

        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        if (!cache.TryGetValue<RemoteEpisode>(CacheKey(release.IndexerId, release.Guid), out var cached) || cached is null)
        {
            return Results.NotFound(new { message = "Couldn't find requested release in cache, try searching again" });
        }

        var remote = cached;
        if (release.EpisodeIds is { Count: > 0 } ids)
        {
            var episodes = await db.Episodes.AsNoTracking().Include(e => e.Series).Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);
            if (episodes.Count != ids.Distinct().Count() || episodes.Select(e => e.SeriesId).Distinct().Count() != 1)
            {
                return ValidationFailure.BadRequest(new ValidationFailure("episodeIds", "Episodes must exist and belong to one series"));
            }

            remote = new RemoteEpisode
            {
                Release = cached.Release,
                Quality = cached.Quality,
                Languages = cached.Languages,
                Series = episodes[0].Series,
                Episodes = episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList(),
                EpisodeRequested = true,
            };
        }

        if (remote.Episodes.Count == 0)
        {
            return Results.NotFound(new { message = "Unable to find matching series and episodes, will need to be manually provided" });
        }

        remote.ReleaseSource = ReleaseSourceType.InteractiveSearch;
        try
        {
            await downloads.GrabAsync(remote, cancellationToken);
        }
        catch (ReleaseDownloadException e)
        {
            return Results.Conflict(new { message = $"Getting release from indexer failed: {e.Message}" });
        }
        catch (DownloadClientException e)
        {
            return Results.Conflict(new { message = e.Message });
        }

        return Results.Ok(release);
    }

    private static string CacheKey(int indexerId, string guid) => $"release:{indexerId}_{guid}";
}
