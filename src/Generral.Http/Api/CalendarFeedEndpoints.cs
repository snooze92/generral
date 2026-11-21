// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Calendar/CalendarFeedController.cs
using System.Globalization;
using Generral.Core.Datastore;
using Generral.Core.Tv;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using IcalCalendar = Ical.Net.Calendar;

namespace Generral.Http.Api;

public static class CalendarFeedEndpoints
{
    public const string CalendarName = "Generral Schedule";

    public static void MapCalendarFeedEndpoints(this IEndpointRouteBuilder feed)
    {
        feed.MapGet("/calendar/generral.ics", GetCalendarFeed);
    }

    /// <summary>Calendar apps can't send headers or cookies: they use <c>?apikey=</c>, and get a 401 (not the login page) without it.</summary>
    private static async Task<IResult> GetCalendarFeed(
        CalendarService calendarService,
        GenerralDbContext db,
        int pastDays = 7,
        int futureDays = 28,
        string? tags = null,
        bool unmonitored = false,
        bool premieresOnly = false,
        bool asAllDay = false)
    {
        var today = calendarService.Today();
        var episodes = await calendarService.EpisodesBetweenAsync(today.AddDays(-pastDays), today.AddDays(futureDays), unmonitored, tags);

        var ids = episodes.Select(e => e.Id).ToList();
        var eventEnds = (await db.EpisodeLinks.AsNoTracking()
                .Where(l => ids.Contains(l.EpisodeId) && l.Event!.End != null)
                .Select(l => new { l.EpisodeId, End = l.Event!.End!.Value })
                .ToListAsync())
            .GroupBy(l => l.EpisodeId)
            .ToDictionary(g => g.Key, g => g.Max(l => l.End));

        var calendar = new IcalCalendar { ProductId = "-//generral//Generral//EN" };
        calendar.AddProperty(new CalendarProperty("NAME", CalendarName));
        calendar.AddProperty(new CalendarProperty("X-WR-CALNAME", CalendarName));

        foreach (var episode in episodes)
        {
            if (premieresOnly && (episode.SeasonNumber == 0 || episode.EpisodeNumber != 1))
            {
                continue;
            }

            calendar.Events.Add(ToEvent(episode, episode.Series!, eventEnds.GetValueOrDefault(episode.Id), asAllDay));
        }

        return Results.Text(new CalendarSerializer().SerializeToString(calendar), "text/calendar; charset=utf-8");
    }

    /// <summary>Ends after the runtime or, without one, at the linked event's end; otherwise the event has no duration.</summary>
    private static CalendarEvent ToEvent(Episode episode, Series series, DateTime? eventEnd, bool asAllDay)
    {
        var airDateUtc = episode.AirDateUtc!.Value;
        var occurrence = new CalendarEvent
        {
            Uid = $"generral_episode_{episode.Id}",
            Status = episode.HasFile ? EventStatus.Confirmed : EventStatus.Tentative,
            Description = episode.Overview,
            Summary = Summary(episode, series),
        };

        if (!string.IsNullOrEmpty(series.Network))
        {
            occurrence.Categories = [series.Network];
        }

        if (asAllDay)
        {
            // The series-local date (spec 05 § Dates and timezones), so a late-evening event stays on its own day.
            occurrence.Start = new CalDateTime(episode.AirDate ?? DateOnly.FromDateTime(airDateUtc));
        }
        else
        {
            occurrence.Start = new CalDateTime(airDateUtc);
            var end = episode.Runtime is int runtime and > 0 ? airDateUtc.AddMinutes(runtime) : eventEnd;
            if (end > airDateUtc)
            {
                occurrence.End = new CalDateTime(end.Value);
            }
        }

        return occurrence;
    }

    private static string Summary(Episode episode, Series series) => series.SeriesType switch
    {
        SeriesType.Daily => $"{series.Title} - {episode.Title}",
        _ => string.Create(CultureInfo.InvariantCulture, $"{series.Title} - S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00} - {episode.Title}"),
    };
}
