// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Tags/TagService.cs
using Generral.Core.Datastore;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Tags;

public sealed record TagDetails(int Id, string Label, IReadOnlyList<int> SourceIds, IReadOnlyList<int> SeriesIds)
{
    public bool InUse => SourceIds.Count > 0 || SeriesIds.Count > 0;
}

/// <summary>Tags are stored lowercase; labels are unique regardless of case.</summary>
public sealed class TagService(GenerralDbContext db)
{
    public Task<List<Tag>> AllAsync() => db.Tags.AsNoTracking().OrderBy(t => t.Label).ToListAsync();

    public Task<Tag?> GetAsync(int id) => db.Tags.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);

    public Task<Tag?> FindByLabelAsync(string label) => db.Tags.AsNoTracking().SingleOrDefaultAsync(t => t.Label == label);

    /// <summary>Returns the existing tag when one has the same label.</summary>
    public async Task<Tag> AddAsync(string label)
    {
        if (await FindByLabelAsync(label) is { } existing)
        {
            return existing;
        }

        var tag = new Tag { Label = label.ToLowerInvariant() };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return tag;
    }

    public async Task<Tag?> UpdateAsync(int id, string label)
    {
        var tag = await db.Tags.SingleOrDefaultAsync(t => t.Id == id);
        if (tag is null)
        {
            return null;
        }

        tag.Label = label.ToLowerInvariant();
        await db.SaveChangesAsync();
        return tag;
    }

    /// <summary>Deletes the tag and removes it from every source and series, atomically.</summary>
    public async Task<bool> DeleteAsync(int id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        var tag = await db.Tags.SingleOrDefaultAsync(t => t.Id == id);
        if (tag is null)
        {
            return false;
        }

        // Tags are JSON columns, so the filtering happens in memory.
        foreach (var source in (await db.Sources.ToListAsync()).Where(s => s.Tags.Contains(id)))
        {
            source.Tags = source.Tags.Where(t => t != id).ToList();
        }

        foreach (var series in (await db.Series.ToListAsync()).Where(s => s.Tags.Contains(id)))
        {
            series.Tags = series.Tags.Where(t => t != id).ToList();
        }

        db.Tags.Remove(tag);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task<List<TagDetails>> DetailsAsync()
    {
        var tags = await AllAsync();
        var sources = await db.Sources.AsNoTracking().Select(s => new { s.Id, s.Tags }).ToListAsync();
        var series = await db.Series.AsNoTracking().Select(s => new { s.Id, s.Tags }).ToListAsync();

        return tags.Select(t => new TagDetails(
            t.Id,
            t.Label,
            sources.Where(s => s.Tags.Contains(t.Id)).Select(s => s.Id).ToList(),
            series.Where(s => s.Tags.Contains(t.Id)).Select(s => s.Id).ToList())).ToList();
    }

    /// <summary>The ids among <paramref name="ids"/> that don't exist.</summary>
    public async Task<List<int>> MissingAsync(IEnumerable<int> ids)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var existing = await db.Tags.Where(t => wanted.Contains(t.Id)).Select(t => t.Id).ToListAsync();
        return wanted.Except(existing).Order().ToList();
    }
}
