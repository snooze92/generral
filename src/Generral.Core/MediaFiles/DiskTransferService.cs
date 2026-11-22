// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Common/Disk/DiskTransferService.cs and src/NzbDrone.Mono/Disk/DiskProvider.cs (TryCreateHardLink)
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Generral.Core.MediaFiles;

public enum TransferMode
{
    Move,
    Copy,
    HardLinkOrCopy,
}

/// <summary>Moves, copies or hardlinks a file (hardlinks fall back to a copy across filesystems), never overwriting the source.</summary>
public sealed partial class DiskTransferService(ILogger<DiskTransferService> logger)
{
    /// <returns>The mode actually used (<see cref="TransferMode.Copy"/> when a hardlink wasn't possible).</returns>
    /// <exception cref="IOException">The transfer failed.</exception>
    public TransferMode TransferFile(string source, string destination, TransferMode mode, bool overwrite = false)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.Ordinal))
        {
            throw new IOException($"Source and destination can't be the same {source}");
        }

        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"File not found: {source}", source);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            if (!overwrite)
            {
                throw new IOException($"Destination already exists: {destination}");
            }

            File.Delete(destination);
        }

        if (mode == TransferMode.HardLinkOrCopy)
        {
            if (TryCreateHardLink(source, destination))
            {
                return TransferMode.HardLinkOrCopy;
            }

            mode = TransferMode.Copy;
        }

        if (mode == TransferMode.Move)
        {
            File.Move(source, destination);
            return mode;
        }

        var temp = destination + ".partial~";
        try
        {
            File.Copy(source, temp, overwrite: true);
            if (new FileInfo(temp).Length != new FileInfo(source).Length)
            {
                throw new IOException($"File copy incomplete: [{source}] to [{destination}]");
            }

            File.Move(temp, destination);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }

        return TransferMode.Copy;
    }

    public bool TryCreateHardLink(string source, string destination)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsFreeBSD())
        {
            return false;
        }

        if (Link(source, destination) == 0)
        {
            return true;
        }

        logger.LogDebug("Hardlink '{Source}' to '{Destination}' failed (errno {Errno}), copying instead", source, destination, Marshal.GetLastPInvokeError());
        return false;
    }

    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Link(string oldPath, string newPath);
}
