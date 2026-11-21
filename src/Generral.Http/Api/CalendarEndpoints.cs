// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Calendar/CalendarController.cs
using Generral.Core.Datastore;
using Generral.Core.Tv;
using Microsoft.AspNetCore.Routing;

namespace Generral.Http.Api;

public static class CalendarEndpoints
{
    public static void MapCalendarEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/calendar", GetCalendar);
    }

    /// <summary>Defaults to today..today + 2 days like Sonarr. Times without an offset are UTC.</summary>
    private static async Task<List<EpisodeResource>> GetCalendar(
        CalendarService calendar,
        GenerralDbContext db,
        TimeProvider time,
        DateTime? start,
        DateTime? end,
        bool unmonitored = false,
        bool includeSeries = false,
        string? tags = null)
    {
        var today = calendar.Today();
        var episodes = await calendar.EpisodesBetweenAsync(Utc(start) ?? today, Utc(end) ?? today.AddDays(2), unmonitored, tags);
        var resources = await EpisodeEndpoints.ToResourcesAsync(db, episodes);
        if (!includeSeries)
        {
            return resources;
        }

        var series = (await SeriesEndpoints.ToResourcesAsync(db, episodes.Select(e => e.Series!).DistinctBy(s => s.Id).ToList(), time))
            .ToDictionary(s => s.Id);
        return resources.Select(r => r with { Series = series[r.SeriesId] }).ToList();
    }

    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        { } other => DateTime.SpecifyKind(other, DateTimeKind.Utc),
    };
}
