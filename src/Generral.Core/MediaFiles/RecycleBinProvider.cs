// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/MediaFiles/RecycleBinProvider.cs
using System.Text.Json.Nodes;
using Generral.Core.Configuration;
using Generral.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Generral.Core.MediaFiles;

public sealed class RecycleBinProvider(DiskTransferService transfer, TimeProvider time, ILogger<RecycleBinProvider> logger)
{
    /// <summary>Moves the file to <c>&lt;recycle bin&gt;/&lt;subfolder&gt;</c> (suffixed <c>_2</c>… when taken), or deletes it without a recycle bin.</summary>
    /// <returns>The recycled path, or null when deleted.</returns>
    /// <exception cref="IOException">The file couldn't be recycled.</exception>
    public string? DeleteFile(MediaManagementConfig config, string path, string subfolder = "")
    {
        if (string.IsNullOrWhiteSpace(config.RecycleBin))
        {
            File.Delete(path);
            logger.LogInformation("Deleted {Path}", path);
            return null;
        }

        var folder = Path.Combine(config.RecycleBin, subfolder);
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var destination = Path.Combine(folder, Path.GetFileName(path));
        for (var index = 2; File.Exists(destination); index++)
        {
            destination = Path.Combine(folder, $"{name}_{index}{extension}");
        }

        transfer.TransferFile(path, destination, TransferMode.Move);
        File.SetLastWriteTimeUtc(destination, time.GetUtcNow().UtcDateTime);
        logger.LogInformation("Recycled {Path} to {Destination}", path, destination);
        return destination;
    }

    /// <summary>Removes recycled files older than the cleanup days, then empty folders.</summary>
    public int Cleanup(MediaManagementConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.RecycleBin) || config.RecycleBinCleanupDays == 0 || !Directory.Exists(config.RecycleBin))
        {
            return 0;
        }

        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-config.RecycleBinCleanupDays);
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(config.RecycleBin, "*", SearchOption.AllDirectories).ToList())
        {
            if (File.GetLastWriteTimeUtc(file) <= cutoff)
            {
                File.Delete(file);
                removed++;
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(config.RecycleBin, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }

        return removed;
    }
}

/// <summary><c>CleanUpRecycleBin</c> (Sonarr's housekeeping task), run daily.</summary>
public sealed class CleanUpRecycleBinHandler(IServiceScopeFactory scopes, RecycleBinProvider recycleBin) : ICommandHandler
{
    public const string CommandName = "CleanUpRecycleBin";

    public string Name => CommandName;

    public async Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var config = await scope.ServiceProvider.GetRequiredService<MediaManagementConfigService>().GetAsync(cancellationToken);
        return $"{recycleBin.Cleanup(config)} file(s) removed from the recycle bin";
    }
}
