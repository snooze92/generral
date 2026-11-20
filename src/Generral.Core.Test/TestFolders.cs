using Generral.Core.Configuration;

namespace Generral.Core.Test;

public sealed class TestFolders : IAppFolders, IDisposable
{
    public TestFolders()
    {
        ConfigFolder = Path.Combine(Path.GetTempPath(), "generral-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ConfigFolder);
    }

    public string ConfigFolder { get; }

    public string ConfigFile => Path.Combine(ConfigFolder, "config.yaml");

    public string LogsFolder => Path.Combine(ConfigFolder, "logs");

    public string DataProtectionFolder => Path.Combine(ConfigFolder, "asp");

    public string UiFolder => Path.Combine(ConfigFolder, "UI");

    public void Dispose()
    {
        Directory.Delete(ConfigFolder, recursive: true);
    }
}
