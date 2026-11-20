namespace Generral.Core.Lifecycle;

public interface IRuntimeInfo
{
    DateTime StartTime { get; }

    bool IsContainerized { get; }
}

public class RuntimeInfo : IRuntimeInfo
{
    public RuntimeInfo()
    {
        StartTime = DateTime.UtcNow;
        IsContainerized = File.Exists("/.dockerenv") ||
                          Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
    }

    public DateTime StartTime { get; }

    public bool IsContainerized { get; }
}
