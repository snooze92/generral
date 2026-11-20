namespace Generral.Core.Configuration;

/// <summary>
/// The effective host-level settings: environment variables &gt; <c>config.yaml</c> &gt; defaults.
/// </summary>
public sealed record HostSettings
{
    public const int DefaultPort = 8990;

    public int Port { get; init; } = DefaultPort;

    public string UrlBase { get; init; } = string.Empty;

    public string BindAddress { get; init; } = "*";

    public string ApiKey { get; init; } = string.Empty;

    public AuthenticationType AuthenticationMethod { get; init; } = AuthenticationType.Forms;

    public AuthenticationRequiredType AuthenticationRequired { get; init; } = AuthenticationRequiredType.Enabled;

    public string LogLevel { get; init; } = "info";

    public DatabaseType DatabaseType { get; init; } = DatabaseType.Sqlite;

    /// <summary>SQLite connection string, or a plain path to the database file.</summary>
    public string DatabaseConnectionString { get; init; } = string.Empty;

    public bool AllowScriptSources { get; init; }

    public string InstanceName { get; init; } = "Generral";
}

/// <summary>Values a user can change from the UI. <c>null</c> means "leave unchanged".</summary>
public sealed record HostSettingsUpdate
{
    public int? Port { get; init; }

    public string? UrlBase { get; init; }

    public string? BindAddress { get; init; }

    public AuthenticationType? AuthenticationMethod { get; init; }

    public AuthenticationRequiredType? AuthenticationRequired { get; init; }

    public string? LogLevel { get; init; }

    public string? InstanceName { get; init; }
}
