using System.Reflection;

namespace Generral.Core.Lifecycle;

public static class BuildInfo
{
    public const string AppName = "Generral";

    static BuildInfo()
    {
        var assembly = typeof(BuildInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        // "1.2.3-beta.4+abcdef0" → version "1.2.3-beta.4", commit "abcdef0"
        var plus = informational.IndexOf('+');
        Version = plus >= 0 ? informational[..plus] : informational;
        Commit = plus >= 0 ? informational[(plus + 1)..] : null;

        BuildTime = File.Exists(assembly.Location) ? File.GetLastWriteTimeUtc(assembly.Location) : DateTime.UtcNow;
    }

    public static string Version { get; }

    public static string? Commit { get; }

    public static DateTime BuildTime { get; }

    public static bool IsDebug =>
#if DEBUG
        true;
#else
        false;
#endif

    public static string UserAgent => $"{AppName}/{Version} (+https://github.com/snooze92/generral)";
}
