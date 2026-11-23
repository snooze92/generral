using System.Globalization;
using System.Runtime.InteropServices;

namespace Generral.Core.MediaFiles;

/// <summary>
/// <i>chown Group</i> (Settings → Media Management → Permissions): changes only the group, keeping the owner. .NET has no managed
/// chown, so this calls libc (same layout of <c>struct group</c> on glibc and musl).
/// </summary>
internal static partial class UnixGroup
{
    private const uint Unchanged = uint.MaxValue;

    /// <summary>A gid, or a group name looked up in the container's <c>/etc/group</c>.</summary>
    public static uint? Resolve(string group)
    {
        if (uint.TryParse(group, NumberStyles.None, CultureInfo.InvariantCulture, out var gid))
        {
            return gid;
        }

        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        var entry = GetGroupByName(group);

        // struct group { char *gr_name; char *gr_passwd; gid_t gr_gid; char **gr_mem; }
        return entry == IntPtr.Zero ? null : (uint)Marshal.ReadInt32(entry, 2 * IntPtr.Size);
    }

    /// <exception cref="IOException">chown failed (not a member of the group, read-only file system, …).</exception>
    public static void Chown(string path, uint gid)
    {
        if (ChangeOwner(path, Unchanged, gid) != 0)
        {
            throw new IOException($"chown to gid {gid} failed (errno {Marshal.GetLastPInvokeError()})");
        }
    }

    [LibraryImport("libc", EntryPoint = "chown", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int ChangeOwner(string path, uint owner, uint group);

    [LibraryImport("libc", EntryPoint = "getgrnam", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetGroupByName(string name);
}
