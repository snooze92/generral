// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Tv/EpisodeRepository.cs (EpisodesBetweenDates)
// and src/Sonarr.Api.V3/Calendar/CalendarController.cs (tag filtering)
using System.Globalization;
using Generral.Core.Datastore;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Tv;

public sealed class CalendarService(GenerralDbContext db, TimeProvider time)
{
    /// <summary>Midnight of the server's local today, in UTC (Sonarr's <c>DateTime.Today</c>).</summary>
    public DateTime Today()
    {
        var now = time.GetLocalNow();
        return new DateTimeOffset(now.Date, now.Offset).UtcDateTime;
    }

    /// <summary>
    /// Episodes airing in <c>[start, end]</c> (UTC), with their series, ordered by air date. Specials are included and, like the
    /// episode list, orphaned episodes without a file are not. <paramref name="tags"/> is Sonarr's comma-separated list of tag ids or
    /// labels; a series matches when it has any of them, and tags that don't exist match nothing.
    /// </summary>
    public async Task<List<Episode>> EpisodesBetweenAsync(DateTime start, DateTime end, bool includeUnmonitored, string? tags, CancellationToken cancellationToken = default)
    {
        var query = db.Episodes.AsNoTracking()
            .Include(e => e.Series)
            .Where(e => e.AirDateUtc >= start && e.AirDateUtc <= end)
            .Where(e => e.Status == EpisodeStatus.Active || e.EpisodeFileId != null);

        if (!includeUnmonitored)
        {
            query = query.Where(e => e.Monitored && e.Series!.Monitored);
        }

        var episodes = await query.ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(tags))
        {
            var tagIds = await ParseTagsAsync(tags, cancellationToken);
            episodes = episodes.Where(e => e.Series!.Tags.Any(tagIds.Contains)).ToList();
        }

        return episodes
            .OrderBy(e => e.AirDateUtc)
            .ThenBy(e => e.SeriesId)
            .ThenBy(e => e.SeasonNumber)
            .ThenBy(e => e.EpisodeNumber)
            .ToList();
    }

    private async Task<HashSet<int>> ParseTagsAsync(string tags, CancellationToken cancellationToken)
    {
        var values = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = values.Where(v => v.All(char.IsAsciiDigit)).Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToList();
        var labels = values.Where(v => !v.All(char.IsAsciiDigit)).Select(v => v.ToLowerInvariant()).ToList();

        return (await db.Tags.AsNoTracking()
            .Where(t => ids.Contains(t.Id) || labels.Contains(t.Label))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
    }
}
