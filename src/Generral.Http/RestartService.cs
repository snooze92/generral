namespace Generral.Http;

public interface IRestartService
{
    bool IsRestartRequested { get; }

    void RequestRestart();
}

/// <summary>
/// In-process restart (Sonarr's System → Restart): stops the web host, and the host's main loop builds a
/// fresh one, which picks up host settings such as port and URL base.
/// </summary>
public class RestartService(IHostApplicationLifetime lifetime) : IRestartService
{
    public bool IsRestartRequested { get; private set; }

    public void RequestRestart()
    {
        IsRestartRequested = true;

        // Let the current response complete first.
        _ = Task.Delay(TimeSpan.FromMilliseconds(500)).ContinueWith(_ => lifetime.StopApplication(), TaskScheduler.Default);
    }
}
