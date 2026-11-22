// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Profiles/Releases/ReleaseProfileService.cs
using Generral.Core.Datastore;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Profiles;

public sealed class ReleaseProfileService(GenerralDbContext db)
{
    public Task<List<ReleaseProfile>> AllAsync() => db.ReleaseProfiles.AsNoTracking().OrderBy(p => p.Id).ToListAsync();

    public Task<ReleaseProfile?> GetAsync(int id) => db.ReleaseProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);

    public async Task<ReleaseProfile> AddAsync(ReleaseProfile profile)
    {
        profile.Id = 0;
        db.ReleaseProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    public async Task<ReleaseProfile?> UpdateAsync(ReleaseProfile profile)
    {
        if (!await db.ReleaseProfiles.AnyAsync(p => p.Id == profile.Id))
        {
            return null;
        }

        db.ReleaseProfiles.Update(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    public async Task<bool> DeleteAsync(int id) => await db.ReleaseProfiles.Where(p => p.Id == id).ExecuteDeleteAsync() > 0;
}
