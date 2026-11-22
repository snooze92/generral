// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Wanted/MissingController.cs and CutoffController.cs
using Generral.Core.Datastore;
using Generral.Core.Tv;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

public static class WantedEndpoints
{
    public static void MapWantedEndpoints(this IEndpointRouteBuilder api)
    {
        var wanted = api.MapGroup("/wanted");
        wanted.MapGet("/missing", GetMissing);
        wanted.MapGet("/cutoff", GetCutoff);
    }

    /// <summary>Aired active episodes without a file; <c>monitored=false</c> lists those whose episode or series is unmonitored.</summary>
    private static async Task<PagingResource<EpisodeResource>> GetMissing(
        GenerralDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 10,
        string? sortKey = null,
        string? sortDirection = null,
        bool includeSeries = false,
        bool monitored = true)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var query = db.Episodes.AsNoTracking().Include(e => e.Series)
            .Where(e => e.Status == EpisodeStatus.Active && e.EpisodeFileId == null && e.AirDateUtc <= now)
            .Where(e => (e.Monitored && e.Series!.Monitored) == monitored);
        var descending = string.Equals(sortDirection, "descending", StringComparison.OrdinalIgnoreCase);
        var key = sortKey ?? "episodes.airDateUtc";
        IOrderedQueryable<Episode> ordered = key switch
        {
            "episodes.lastSearchTime" or "lastSearchTime" => descending ? query.OrderByDescending(e => e.LastSearchTime) : query.OrderBy(e => e.LastSearchTime),
            "series.sortTitle" or "sortTitle" => descending ? query.OrderByDescending(e => e.Series!.SortTitle) : query.OrderBy(e => e.Series!.SortTitle),
            _ => descending ? query.OrderByDescending(e => e.AirDateUtc) : query.OrderBy(e => e.AirDateUtc),
        };
        ordered = ordered.ThenBy(e => e.Id);

        var size = Math.Clamp(pageSize, 1, 1000);
        var current = Math.Max(page, 1);
        var total = await query.CountAsync(cancellationToken);
        var episodes = await ordered.Skip((current - 1) * size).Take(size).ToListAsync(cancellationToken);
        var resources = await EpisodeEndpoints.ToResourcesAsync(db, episodes);
        if (includeSeries)
        {
            var series = (await SeriesEndpoints.ToResourcesAsync(db, episodes.Select(e => e.Series!).DistinctBy(s => s.Id).ToList(), time)).ToDictionary(s => s.Id);
            resources = resources.Select(r => r with { Series = series[r.SeriesId] }).ToList();
        }

        return new PagingResource<EpisodeResource>(current, size, key, descending ? SortDirection.Descending : SortDirection.Ascending, total, resources);
    }

    /// <summary>Episodes whose file is below the profile cutoff: always empty until episode files exist (M3).</summary>
    private static PagingResource<EpisodeResource> GetCutoff(int page = 1, int pageSize = 10, string? sortKey = null, string? sortDirection = null) =>
        new(Math.Max(page, 1), Math.Clamp(pageSize, 1, 1000), sortKey ?? "episodes.airDateUtc",
            string.Equals(sortDirection, "descending", StringComparison.OrdinalIgnoreCase) ? SortDirection.Descending : SortDirection.Ascending, 0, []);
}
