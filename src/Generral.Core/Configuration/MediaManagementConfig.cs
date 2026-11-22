// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Config/MediaManagementConfigResource.cs (subset) and src/NzbDrone.Core/Configuration/ConfigService.cs (defaults)
using Generral.Core.Datastore;

namespace Generral.Core.Configuration;

/// <summary>Settings → Media Management (spec 08): the import and file-management subset of Sonarr's.</summary>
public sealed record MediaManagementConfig
{
    /// <summary>Hardlink instead of copying from the download client when on the same filesystem (spec 08 default on).</summary>
    public bool CopyUsingHardlinks { get; init; } = true;

    /// <summary>Old files go here on upgrade or delete; empty deletes them.</summary>
    public string RecycleBin { get; init; } = "";

    /// <summary>Days before files in the recycle bin are removed; 0 disables the cleanup.</summary>
    public int RecycleBinCleanupDays { get; init; } = 7;

    public bool SkipFreeSpaceCheckWhenImporting { get; init; }

    /// <summary>Megabytes that must stay free after an import.</summary>
    public int MinimumFreeSpaceWhenImporting { get; init; } = 100;

    /// <summary>Carry subtitle files over with the video (spec 08 step 8).</summary>
    public bool ImportExtraFiles { get; init; } = true;

    public string ExtraFileExtensions { get; init; } = "srt,ass,ssa,sub,idx,vtt";

    public bool DeleteEmptyFolders { get; init; }

    public bool SetPermissionsLinux { get; init; }

    public string ChmodFolder { get; init; } = "755";

    public string ChownGroup { get; init; } = "";

    public string? Validate()
    {
        if (RecycleBinCleanupDays is < 0 or > 365)
        {
            return "Recycle bin cleanup days must be between 0 and 365";
        }

        if (MinimumFreeSpaceWhenImporting < 0)
        {
            return "Minimum free space must be positive";
        }

        if (!string.IsNullOrWhiteSpace(RecycleBin) && !Path.IsPathFullyQualified(RecycleBin))
        {
            return "Recycle bin must be an absolute path";
        }

        return SetPermissionsLinux && (ChmodFolder.Length is < 3 or > 4 || ChmodFolder.Any(c => c is < '0' or > '7'))
            ? "Folder permissions must be an octal mode such as 755"
            : null;
    }
}

public sealed class MediaManagementConfigService(GenerralDbContext db) : JsonConfigService<MediaManagementConfig>(db, "mediamanagement")
{
    protected override string? Validate(MediaManagementConfig config) => config.Validate();
}
