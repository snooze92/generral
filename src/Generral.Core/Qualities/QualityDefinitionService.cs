// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Qualities/QualityDefinitionService.cs
using Generral.Core.Datastore;
using Generral.Core.Profiles;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Qualities;

public sealed class QualityDefinitionService(GenerralDbContext db)
{
    /// <summary>By weight, with <c>Weight</c> and <c>GroupName</c> from the built-in list.</summary>
    public async Task<List<QualityDefinition>> AllAsync()
    {
        var all = await db.QualityDefinitions.AsNoTracking().ToListAsync();
        all.ForEach(WithDefaults);
        return all.OrderBy(d => d.Weight).ThenBy(d => d.Id).ToList();
    }

    public async Task<QualityDefinition?> GetAsync(int id) => (await AllAsync()).SingleOrDefault(d => d.Id == id);

    /// <summary>Updates title and size limits by id, and copies the limits into every quality profile, like Sonarr.</summary>
    public async Task<List<QualityDefinition>> UpdateManyAsync(IReadOnlyList<QualityDefinition> definitions)
    {
        var ids = definitions.Select(d => d.Id).ToList();
        var stored = await db.QualityDefinitions.Where(d => ids.Contains(d.Id)).ToListAsync();
        foreach (var definition in stored)
        {
            var update = definitions.Last(d => d.Id == definition.Id);
            definition.Title = update.Title;
            definition.MinSize = update.MinSize;
            definition.MaxSize = update.MaxSize;
            definition.PreferredSize = update.PreferredSize;
        }

        var profiles = await db.QualityProfiles.ToListAsync();
        foreach (var profile in profiles)
        {
            profile.Items = CopySizes(profile.Items, stored);
        }

        await db.SaveChangesAsync();
        stored.ForEach(WithDefaults);
        return stored;
    }

    /// <summary>Inserts missing definitions and removes unknown ones (Sonarr's <c>InsertMissingDefinitions</c>).</summary>
    public async Task EnsureDefaultsAsync()
    {
        var existing = await db.QualityDefinitions.ToListAsync();
        foreach (var definition in Quality.DefaultQualityDefinitions)
        {
            if (existing.All(d => d.Quality != definition.Quality))
            {
                db.QualityDefinitions.Add(new QualityDefinition(definition.Quality)
                {
                    MinSize = definition.MinSize,
                    MaxSize = definition.MaxSize,
                    PreferredSize = definition.PreferredSize,
                });
            }
        }

        db.QualityDefinitions.RemoveRange(existing.Where(d => Quality.DefaultQualityDefinitions.All(q => q.Quality != d.Quality)));
        await db.SaveChangesAsync();
    }

    private static void WithDefaults(QualityDefinition definition)
    {
        var builtIn = Quality.DefaultQualityDefinitions.Single(d => d.Quality == definition.Quality);
        definition.Weight = builtIn.Weight;
        definition.GroupName = builtIn.GroupName;
    }

    private static List<QualityProfileQualityItem> CopySizes(List<QualityProfileQualityItem> items, List<QualityDefinition> definitions) =>
        items.Select(item =>
        {
            var copy = new QualityProfileQualityItem
            {
                Id = item.Id,
                Name = item.Name,
                Quality = item.Quality,
                Allowed = item.Allowed,
                Items = CopySizes(item.Items, definitions),
                MinSize = item.MinSize,
                MaxSize = item.MaxSize,
                PreferredSize = item.PreferredSize,
            };

            if (item.Quality is { } quality && definitions.SingleOrDefault(d => d.Quality == quality) is { } definition)
            {
                copy.MinSize = definition.MinSize;
                copy.MaxSize = definition.MaxSize;
                copy.PreferredSize = definition.PreferredSize;
            }

            return copy;
        }).ToList();
}
