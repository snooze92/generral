// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/RemotePathMappings/RemotePathMapping.cs and RemotePathMappingService.cs
using Generral.Core.Datastore;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.RemotePathMappings;

/// <summary>Translates a path reported by the download client on <see cref="Host"/> into the path Generral sees.</summary>
public class RemotePathMapping
{
    public int Id { get; set; }

    public required string Host { get; set; }

    /// <summary>Stored with a trailing separator.</summary>
    public required string RemotePath { get; set; }

    /// <summary>Stored with a trailing separator.</summary>
    public required string LocalPath { get; set; }

    public static string WithTrailingSlash(string path)
    {
        path = path.Trim();
        var separator = path.Contains('\\') && !path.Contains('/') ? '\\' : '/';
        return path.EndsWith('/') || path.EndsWith('\\') ? path : path + separator;
    }
}

public sealed class RemotePathMappingService(GenerralDbContext db)
{
    public async Task<string> RemapRemoteToLocalAsync(string host, string remotePath, CancellationToken cancellationToken = default) =>
        RemapRemoteToLocal(await db.RemotePathMappings.AsNoTracking().ToListAsync(cancellationToken), host, remotePath);

    /// <summary>The longest matching remote path wins; the comparison is case-sensitive except for Windows paths.</summary>
    public static string RemapRemoteToLocal(IEnumerable<RemotePathMapping> mappings, string host, string remotePath)
    {
        foreach (var mapping in mappings.Where(m => string.Equals(m.Host, host, StringComparison.OrdinalIgnoreCase)).OrderByDescending(m => m.RemotePath.Length))
        {
            var comparison = mapping.RemotePath.Contains('\\') ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var remote = mapping.RemotePath;
            if (string.Equals(RemotePathMapping.WithTrailingSlash(remotePath), remote, comparison))
            {
                return mapping.LocalPath.TrimEnd('/', '\\') is { Length: > 0 } local ? local : mapping.LocalPath;
            }

            if (remotePath.StartsWith(remote, comparison))
            {
                var rest = remotePath[remote.Length..].Replace('\\', System.IO.Path.DirectorySeparatorChar);
                return System.IO.Path.Combine(mapping.LocalPath, rest);
            }
        }

        return remotePath;
    }
}
