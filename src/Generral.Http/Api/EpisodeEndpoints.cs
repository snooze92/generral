using Generral.Core.Datastore;
using Generral.Core.Mappings;
using Generral.Core.Tv;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

/// <summary>Sonarr's EpisodeResource (no scene numbering, file or series) plus Generral's lock, origin, status and event links.</summary>
public sealed record EpisodeResource
{
    public int Id { get; init; }

    public int SeriesId { get; init; }

    public int EpisodeFileId { get; init; }

    public int SeasonNumber { get; init; }

    public int EpisodeNumber { get; init; }

    public string? Title { get; init; }

    /// <summary><c>yyyy-MM-dd</c> in the series timezone.</summary>
    public string? AirDate { get; init; }

    public DateTime? AirDateUtc { get; init; }

    public DateTime? LastSearchTime { get; init; }

    public int Runtime { get; init; }

    public string? Overview { get; init; }

    public bool HasFile { get; init; }

    public bool Monitored { get; init; }

    public int? AbsoluteEpisodeNumber { get; init; }

    public bool UnverifiedSceneNumbering => false;

    public bool Grabbed => false;

    public List<MediaCoverResource> Images { get; init; } = [];

    public bool NumberLocked { get; init; }

    public EpisodeSource Source { get; init; }

    public EpisodeStatus Status { get; init; }

    public List<EpisodeLinkResource> Links { get; init; } = [];
}

/// <summary>The event (and part) an episode was produced from.</summary>
public sealed record EpisodeLinkResource(int MappingId, int EventId, string? PartKey);

public sealed record EpisodesMonitoredResource(List<int>? EpisodeIds, bool Monitored);

public sealed record EpisodeLockResource(bool NumberLocked);

public static class EpisodeEndpoints
{
    public static void MapEpisodeEndpoints(this IEndpointRouteBuilder api)
    {
        var episodes = api.MapGroup("/episode");

        episodes.MapGet("/", GetAll);
        episodes.MapGet("/{id:int}", Get);
        episodes.MapPut("/{id:int}", SetMonitored);
        episodes.MapPut("/monitor", SetManyMonitored);
        episodes.MapPut("/{id:int}/lock", SetLocked);
    }

    /// <summary>Like Sonarr, needs <c>seriesId</c> (optionally with <c>seasonNumber</c>) or <c>episodeIds</c>. Orphaned episodes without a file are hidden unless asked for.</summary>
    private static async Task<IResult> GetAll(GenerralDbContext db, int? seriesId, int? seasonNumber, [FromQuery] int[]? episodeIds, bool includeOrphaned = false)
    {
        IQueryable<Episode> query = db.Episodes.AsNoTracking();
        if (seriesId is { } series)
        {
            query = query.Where(e => e.SeriesId == series);
            if (seasonNumber is { } season)
            {
                query = query.Where(e => e.SeasonNumber == season);
            }
        }
        else if (episodeIds is { Length: > 0 })
        {
            query = query.Where(e => episodeIds.Contains(e.Id));
        }
        else
        {
            return ValidationFailure.BadRequest(new ValidationFailure("seriesId", "seriesId or episodeIds must be provided"));
        }

        if (!includeOrphaned)
        {
            query = query.Where(e => e.Status == EpisodeStatus.Active || e.EpisodeFileId != null);
        }

        var episodes = await query.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToListAsync();
        return Results.Ok(await ToResourcesAsync(db, episodes));
    }

    private static async Task<IResult> Get(int id, GenerralDbContext db)
    {
        var episode = await db.Episodes.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
        return episode is null ? Results.NotFound() : Results.Ok((await ToResourcesAsync(db, [episode]))[0]);
    }

    /// <summary>Like Sonarr, only <c>monitored</c> is read.</summary>
    private static async Task<IResult> SetMonitored(int id, EpisodeResource resource, GenerralDbContext db)
    {
        var episode = await db.Episodes.SingleOrDefaultAsync(e => e.Id == id);
        if (episode is null)
        {
            return Results.NotFound();
        }

        episode.Monitored = resource.Monitored;
        await db.SaveChangesAsync();
        return Results.Accepted($"episode/{id}", (await ToResourcesAsync(db, [episode]))[0]);
    }

    /// <summary>Unknown ids are ignored, as in Sonarr.</summary>
    private static async Task<IResult> SetManyMonitored(EpisodesMonitoredResource resource, GenerralDbContext db)
    {
        var ids = resource.EpisodeIds ?? [];
        if (ids.Count == 0)
        {
            return ValidationFailure.BadRequest(new ValidationFailure("episodeIds", "'Episode Ids' must not be empty."));
        }

        var episodes = await db.Episodes.Where(e => ids.Contains(e.Id)).ToListAsync();
        foreach (var episode in episodes)
        {
            episode.Monitored = resource.Monitored;
        }

        await db.SaveChangesAsync();
        return Results.Accepted("episode", await ToResourcesAsync(db, episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList()));
    }

    /// <summary>A locked number is never changed by mappings (spec 05 § Numbering stability).</summary>
    private static async Task<IResult> SetLocked(int id, EpisodeLockResource resource, GenerralDbContext db)
    {
        var episode = await db.Episodes.SingleOrDefaultAsync(e => e.Id == id);
        if (episode is null)
        {
            return Results.NotFound();
        }

        episode.NumberLocked = resource.NumberLocked;
        await db.SaveChangesAsync();
        return Results.Accepted($"episode/{id}", (await ToResourcesAsync(db, [episode]))[0]);
    }

    private static async Task<List<EpisodeResource>> ToResourcesAsync(GenerralDbContext db, List<Episode> episodes)
    {
        var ids = episodes.Select(e => e.Id).ToList();
        var links = (await db.EpisodeLinks.AsNoTracking().Where(l => ids.Contains(l.EpisodeId)).ToListAsync())
            .OrderBy(l => l.MappingId).ThenBy(l => l.EventId).ThenBy(l => l.PartKey, StringComparer.Ordinal)
            .ToLookup(l => l.EpisodeId);

        return episodes.Select(e => new EpisodeResource
        {
            Id = e.Id,
            SeriesId = e.SeriesId,
            EpisodeFileId = e.EpisodeFileId ?? 0,
            SeasonNumber = e.SeasonNumber,
            EpisodeNumber = e.EpisodeNumber,
            Title = e.Title,
            AirDate = e.AirDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            AirDateUtc = e.AirDateUtc,
            LastSearchTime = e.LastSearchTime,
            Runtime = e.Runtime ?? 0,
            Overview = e.Overview,
            HasFile = e.HasFile,
            Monitored = e.Monitored,
            AbsoluteEpisodeNumber = e.AbsoluteEpisodeNumber,
            Images = MediaCoverResource.From(e.Images),
            NumberLocked = e.NumberLocked,
            Source = e.Source,
            Status = e.Status,
            Links = links[e.Id].Select(l => new EpisodeLinkResource(l.MappingId, l.EventId, l.PartKey == EpisodeLink.NoPart ? null : l.PartKey)).ToList(),
        }).ToList();
    }
}
