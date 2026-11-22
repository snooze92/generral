// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Tv/EpisodeCutoffService.cs
using Generral.Core.Datastore;
using Generral.Core.DecisionEngine;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Tv;

public static class EpisodeCutoffService
{
    /// <summary>Active episodes whose file is below the cutoff of their series' quality profile.</summary>
    public static async Task<HashSet<int>> CutoffUnmetEpisodeIdsAsync(GenerralDbContext db, CancellationToken cancellationToken = default)
    {
        var profiles = await db.QualityProfiles.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);
        var rows = await (from episode in db.Episodes.AsNoTracking()
                          join file in db.EpisodeFiles.AsNoTracking() on episode.EpisodeFileId equals file.Id
                          where episode.Status == EpisodeStatus.Active
                          select new { episode.Id, episode.Series!.QualityProfileId, file.Quality })
            .ToListAsync(cancellationToken);
        return rows.Where(r => profiles.TryGetValue(r.QualityProfileId, out var profile) && UpgradableSpecification.QualityCutoffNotMet(profile, r.Quality))
            .Select(r => r.Id)
            .ToHashSet();
    }
}
