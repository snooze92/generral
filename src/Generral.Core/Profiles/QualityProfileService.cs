// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Profiles/Qualities/QualityProfileService.cs
using Generral.Core.Datastore;
using Generral.Core.Qualities;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Profiles;

public sealed class QualityProfileInUseException(string name) : Exception($"QualityProfile [{name}] is in use.");

public sealed class QualityProfileService(GenerralDbContext db)
{
    public Task<List<QualityProfile>> AllAsync() => db.QualityProfiles.AsNoTracking().OrderBy(p => p.Id).ToListAsync();

    public Task<QualityProfile?> GetAsync(int id) => db.QualityProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);

    public Task<bool> ExistsAsync(int id) => db.QualityProfiles.AnyAsync(p => p.Id == id);

    /// <summary>The profile new series get when none is given: the lowest id.</summary>
    public Task<int> DefaultIdAsync() => db.QualityProfiles.OrderBy(p => p.Id).Select(p => p.Id).FirstOrDefaultAsync();

    public async Task<QualityProfile> AddAsync(QualityProfile profile)
    {
        profile.Id = 0;
        db.QualityProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    public async Task<QualityProfile?> UpdateAsync(QualityProfile profile)
    {
        var stored = await db.QualityProfiles.SingleOrDefaultAsync(p => p.Id == profile.Id);
        if (stored is null)
        {
            return null;
        }

        stored.Name = profile.Name;
        stored.UpgradeAllowed = profile.UpgradeAllowed;
        stored.Cutoff = profile.Cutoff;
        stored.Items = profile.Items;
        await db.SaveChangesAsync();
        return stored;
    }

    /// <exception cref="QualityProfileInUseException">A series uses the profile.</exception>
    public async Task<bool> DeleteAsync(int id)
    {
        var profile = await db.QualityProfiles.SingleOrDefaultAsync(p => p.Id == id);
        if (profile is null)
        {
            return false;
        }

        if (await db.Series.AnyAsync(s => s.QualityProfileId == id))
        {
            throw new QualityProfileInUseException(profile.Name);
        }

        db.QualityProfiles.Remove(profile);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Seeds Sonarr's default profiles when there are none, with sizes from the stored quality definitions, and
    /// points series without a valid profile (created before profiles existed) at the default one.
    /// </summary>
    public async Task EnsureDefaultsAsync()
    {
        if (!await db.QualityProfiles.AnyAsync())
        {
            await AddDefaultsAsync();
        }

        var defaultId = await DefaultIdAsync();
        await db.Series.Where(s => !db.QualityProfiles.Any(p => p.Id == s.QualityProfileId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.QualityProfileId, defaultId));
    }

    private async Task AddDefaultsAsync()
    {

        var definitions = await db.QualityDefinitions.AsNoTracking().ToListAsync();

        Add("Any", Quality.SDTV, Quality.SDTV, Quality.WEBRip480p, Quality.WEBDL480p, Quality.DVD, Quality.Bluray480p, Quality.Bluray576p, Quality.HDTV720p,
            Quality.HDTV1080p, Quality.WEBRip720p, Quality.WEBDL720p, Quality.WEBRip1080p, Quality.WEBDL1080p, Quality.Bluray720p, Quality.Bluray1080p);
        Add("SD", Quality.SDTV, Quality.SDTV, Quality.WEBRip480p, Quality.WEBDL480p, Quality.DVD, Quality.Bluray480p, Quality.Bluray576p);
        Add("HD-720p", Quality.HDTV720p, Quality.HDTV720p, Quality.WEBRip720p, Quality.WEBDL720p, Quality.Bluray720p);
        Add("HD-1080p", Quality.HDTV1080p, Quality.HDTV1080p, Quality.WEBRip1080p, Quality.WEBDL1080p, Quality.Bluray1080p);
        Add("Ultra-HD", Quality.HDTV2160p, Quality.HDTV2160p, Quality.WEBRip2160p, Quality.WEBDL2160p, Quality.Bluray2160p);
        Add("HD - 720p/1080p", Quality.HDTV720p, Quality.HDTV720p, Quality.HDTV1080p, Quality.WEBRip720p, Quality.WEBDL720p, Quality.WEBRip1080p,
            Quality.WEBDL1080p, Quality.Bluray720p, Quality.Bluray1080p);
        await db.SaveChangesAsync();

        void Add(string name, Quality cutoff, params Quality[] allowed) => db.QualityProfiles.Add(GetDefaultProfile(name, definitions, cutoff, allowed));
    }

    /// <summary>Every quality, grouped and ordered by weight; also the <c>/qualityprofile/schema</c> template.</summary>
    public static QualityProfile GetDefaultProfile(string name, IReadOnlyList<QualityDefinition> definitions, Quality? cutoff = null, params Quality[] allowed)
    {
        var items = new List<QualityProfileQualityItem>();
        var groupId = 1000;
        var profileCutoff = cutoff?.Id ?? Quality.Unknown.Id;

        foreach (var group in Quality.DefaultQualityDefinitions.GroupBy(q => q.Weight))
        {
            if (group.Count() == 1)
            {
                items.Add(Item(group.First().Quality, allowed.Contains(group.First().Quality)));
                continue;
            }

            var groupAllowed = group.Any(g => allowed.Contains(g.Quality));
            items.Add(new QualityProfileQualityItem
            {
                Id = groupId,
                Name = group.First().GroupName,
                Items = group.Select(g => Item(g.Quality, groupAllowed)).ToList(),
                Allowed = groupAllowed,
            });

            if (group.Any(g => g.Quality.Id == profileCutoff))
            {
                profileCutoff = groupId;
            }

            groupId++;
        }

        return new QualityProfile { Name = name, Cutoff = profileCutoff, Items = items };

        QualityProfileQualityItem Item(Quality quality, bool isAllowed)
        {
            var definition = definitions.FirstOrDefault(d => d.Quality == quality) ?? Quality.DefaultQualityDefinitions.Single(d => d.Quality == quality);
            return new QualityProfileQualityItem
            {
                Quality = quality,
                Allowed = isAllowed,
                MinSize = definition.MinSize,
                MaxSize = definition.MaxSize,
                PreferredSize = definition.PreferredSize,
            };
        }
    }
}
