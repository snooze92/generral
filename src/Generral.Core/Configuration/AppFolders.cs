namespace Generral.Core.Configuration;

public interface IAppFolders
{
    string ConfigFolder { get; }

    string ConfigFile { get; }

    string LogsFolder { get; }

    string DataProtectionFolder { get; }

    string UiFolder { get; }
}

public class AppFolders : IAppFolders
{
    /// <summary>Configuration key (from <c>GENERRAL__CONFIGDIR</c>) for the config folder.</summary>
    public const string ConfigDirKey = "GENERRAL:CONFIGDIR";

    /// <summary>Configuration key (from <c>GENERRAL__UIDIR</c>) for the built SPA; defaults to <c>&lt;app&gt;/UI</c>.</summary>
    public const string UiDirKey = "GENERRAL:UIDIR";

    public AppFolders(string? configFolder = null, string? uiFolder = null)
    {
        ConfigFolder = Path.GetFullPath(string.IsNullOrWhiteSpace(configFolder) ? DefaultConfigFolder() : configFolder);
        UiFolder = Path.GetFullPath(string.IsNullOrWhiteSpace(uiFolder) ? Path.Combine(AppContext.BaseDirectory, "UI") : uiFolder);

        Directory.CreateDirectory(ConfigFolder);
        Directory.CreateDirectory(LogsFolder);
    }

    public string ConfigFolder { get; }

    public string ConfigFile => Path.Combine(ConfigFolder, "config.yaml");

    public string LogsFolder => Path.Combine(ConfigFolder, "logs");

    public string DataProtectionFolder => Path.Combine(ConfigFolder, "asp");

    public string UiFolder { get; }

    private static string DefaultConfigFolder()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var root = string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : xdg;

        return Path.Combine(root, "Generral");
    }
}
