using Generral.Core.Datastore;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Mappings;

/// <summary>A saved definition of a mapping, kept for diff and rollback. Unique on <c>(MappingId, Version)</c>.</summary>
public class MappingVersion
{
    public int Id { get; set; }

    public int MappingId { get; set; }

    public Mapping? Mapping { get; set; }

    public int Version { get; set; }

    public string Definition { get; set; } = "";

    public DateTime Created { get; set; } = DateTime.UtcNow;
}

public static class MappingHistory
{
    /// <summary>
    /// Bumps <see cref="Mapping.Version"/> and keeps a <see cref="MappingVersion"/> whenever the definition differs from the last one kept
    /// (compared verbatim). Works for unsaved mappings; the caller saves.
    /// </summary>
    public static async Task RecordAsync(GenerralDbContext db, Mapping mapping, CancellationToken cancellationToken = default)
    {
        var last = mapping.Id == 0
            ? null
            : await db.MappingVersions.Where(v => v.MappingId == mapping.Id).OrderByDescending(v => v.Version).FirstOrDefaultAsync(cancellationToken);
        if (last is not null && last.Definition == mapping.Definition)
        {
            return;
        }

        if (last is not null)
        {
            mapping.Version = Math.Max(mapping.Version, last.Version + 1);
        }

        db.MappingVersions.Add(new MappingVersion { Mapping = mapping, Version = mapping.Version, Definition = mapping.Definition });
    }
}
