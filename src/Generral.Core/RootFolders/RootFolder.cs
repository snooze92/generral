// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/RootFolders/RootFolder.cs and RootFolderService.cs
using Generral.Core.Datastore;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.RootFolders;

public class RootFolder
{
    public int Id { get; set; }

    public required string Path { get; set; }
}

public sealed record RootFolderInfo(RootFolder Folder, bool Accessible, long? FreeSpace, long? TotalSpace, IReadOnlyList<string> UnmappedFolders);

public sealed class RootFolderService(GenerralDbContext db)
{
    public async Task<List<RootFolderInfo>> AllAsync(CancellationToken cancellationToken = default)
    {
        var folders = await db.RootFolders.AsNoTracking().OrderBy(r => r.Path).ToListAsync(cancellationToken);
        var seriesPaths = (await db.Series.AsNoTracking().Select(s => s.Path).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        return folders.Select(f => Describe(f, seriesPaths)).ToList();
    }

    public async Task<RootFolderInfo?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        (await AllAsync(cancellationToken)).SingleOrDefault(r => r.Folder.Id == id);

    public static RootFolderInfo Describe(RootFolder folder, IReadOnlySet<string> seriesPaths)
    {
        if (!Directory.Exists(folder.Path))
        {
            return new RootFolderInfo(folder, false, null, null, []);
        }

        var unmapped = Directory.EnumerateDirectories(folder.Path)
            .Where(d => !seriesPaths.Contains(d) && !System.IO.Path.GetFileName(d).StartsWith('.'))
            .Order(StringComparer.Ordinal)
            .ToList();
        var (free, total) = DiskSpace(folder.Path);
        return new RootFolderInfo(folder, true, free, total, unmapped);
    }

    /// <returns>Free and total bytes of the drive holding <paramref name="path"/>, or nulls when unknown.</returns>
    public static (long? Free, long? Total) DiskSpace(string path)
    {
        try
        {
            var drive = new DriveInfo(System.IO.Path.GetFullPath(path));
            return (drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }
}
