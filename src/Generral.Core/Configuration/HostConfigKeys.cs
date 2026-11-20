namespace Generral.Core.Configuration;

/// <summary>A host-level setting: its <c>config.yaml</c> key and its environment variable.</summary>
public sealed record HostConfigKey(string YamlKey, string EnvironmentVariable)
{
    public static readonly HostConfigKey Port = new("port", "GENERRAL__PORT");
    public static readonly HostConfigKey UrlBase = new("urlBase", "GENERRAL__URLBASE");
    public static readonly HostConfigKey BindAddress = new("bindAddress", "GENERRAL__BINDADDRESS");
    public static readonly HostConfigKey ApiKey = new("apiKey", "GENERRAL__APIKEY");
    public static readonly HostConfigKey AuthenticationMethod = new("authenticationMethod", "GENERRAL__AUTHMETHOD");
    public static readonly HostConfigKey AuthenticationRequired = new("authenticationRequired", "GENERRAL__AUTHREQUIRED");
    public static readonly HostConfigKey LogLevel = new("logLevel", "GENERRAL__LOGLEVEL");
    public static readonly HostConfigKey DatabaseType = new("database.type", "GENERRAL__DB__TYPE");
    public static readonly HostConfigKey DatabaseConnectionString = new("database.connectionString", "GENERRAL__DB__CONNECTIONSTRING");
    public static readonly HostConfigKey AllowScriptSources = new("allowScriptSources", "GENERRAL__ALLOWSCRIPTSOURCES");
    public static readonly HostConfigKey InstanceName = new("instanceName", "GENERRAL__INSTANCENAME");

    public static IReadOnlyList<HostConfigKey> All { get; } =
    [
        Port, UrlBase, BindAddress, ApiKey, AuthenticationMethod, AuthenticationRequired, LogLevel,
        DatabaseType, DatabaseConnectionString, AllowScriptSources, InstanceName
    ];
}
