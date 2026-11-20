using System.Security.Cryptography;

namespace Generral.Core.Configuration;

public interface IHostConfigProvider
{
    HostSettings Current { get; }

    /// <summary>Settings fixed by environment variables, which the UI must not try to change.</summary>
    bool IsOverriddenByEnvironment(HostConfigKey key);

    HostSettings Update(HostSettingsUpdate update);

    HostSettings ResetApiKey();

    /// <summary>True when this start created <c>config.yaml</c> or a new API key (so the host can log it).</summary>
    bool GeneratedApiKey { get; }

    event EventHandler<HostSettings>? Changed;
}

/// <summary>
/// Reads host-level settings with the precedence environment &gt; <c>config.yaml</c> &gt; defaults,
/// and writes back to <c>config.yaml</c> (creating it, with a generated API key, on first start).
/// </summary>
public sealed class HostConfigProvider : IHostConfigProvider
{
    private readonly IAppFolders _folders;
    private readonly IReadOnlyDictionary<string, string> _environment;
    private readonly Lock _lock = new();

    private HostConfigFile _file;
    private HostSettings _current;

    public HostConfigProvider(IAppFolders folders, IReadOnlyDictionary<string, string> environment)
    {
        _folders = folders;
        _environment = environment;

        _file = Load();
        EnsureApiKey();
        _current = Resolve(_file, _environment);
    }

    public event EventHandler<HostSettings>? Changed;

    public bool GeneratedApiKey { get; private set; }

    public HostSettings Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public bool IsOverriddenByEnvironment(HostConfigKey key)
    {
        return _environment.TryGetValue(key.EnvironmentVariable, out var value) && !string.IsNullOrWhiteSpace(value);
    }

    public HostSettings Update(HostSettingsUpdate update)
    {
        HostSettings updated;

        lock (_lock)
        {
            if (update.Port is { } port)
            {
                if (port is < 1 or > 65535)
                {
                    throw new ArgumentOutOfRangeException(nameof(update), port, "Port must be between 1 and 65535");
                }

                _file.Port = port;
            }

            if (update.UrlBase is not null)
            {
                _file.UrlBase = NormalizeUrlBase(update.UrlBase);
            }

            if (update.BindAddress is not null)
            {
                _file.BindAddress = update.BindAddress.Trim();
            }

            if (update.AuthenticationMethod is { } method)
            {
                _file.AuthenticationMethod = method.ToString();
            }

            if (update.AuthenticationRequired is { } required)
            {
                _file.AuthenticationRequired = required.ToString();
            }

            if (update.LogLevel is not null)
            {
                _file.LogLevel = ParseLogLevel(update.LogLevel);
            }

            if (update.InstanceName is not null)
            {
                _file.InstanceName = update.InstanceName.Trim();
            }

            Save();
            updated = _current = Resolve(_file, _environment);
        }

        Changed?.Invoke(this, updated);

        return updated;
    }

    public HostSettings ResetApiKey()
    {
        HostSettings updated;

        lock (_lock)
        {
            if (IsOverriddenByEnvironment(HostConfigKey.ApiKey))
            {
                throw new InvalidOperationException($"The API key is set by {HostConfigKey.ApiKey.EnvironmentVariable} and can't be reset from Generral");
            }

            _file.ApiKey = GenerateApiKey();
            Save();
            updated = _current = Resolve(_file, _environment);
        }

        Changed?.Invoke(this, updated);

        return updated;
    }

    public static string GenerateApiKey()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    }

    internal static HostSettings Resolve(HostConfigFile file, IReadOnlyDictionary<string, string> environment)
    {
        string? Env(HostConfigKey key)
        {
            return environment.TryGetValue(key.EnvironmentVariable, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;
        }

        var defaults = new HostSettings();

        var portText = Env(HostConfigKey.Port);
        var port = portText is not null ? ParseInt(portText, HostConfigKey.Port) : file.Port ?? defaults.Port;

        return new HostSettings
        {
            Port = port,
            UrlBase = NormalizeUrlBase(Env(HostConfigKey.UrlBase) ?? file.UrlBase ?? defaults.UrlBase),
            BindAddress = Env(HostConfigKey.BindAddress) ?? NullIfBlank(file.BindAddress) ?? defaults.BindAddress,
            ApiKey = Env(HostConfigKey.ApiKey) ?? file.ApiKey ?? defaults.ApiKey,
            AuthenticationMethod = ParseEnum(Env(HostConfigKey.AuthenticationMethod) ?? file.AuthenticationMethod, defaults.AuthenticationMethod, HostConfigKey.AuthenticationMethod),
            AuthenticationRequired = ParseEnum(Env(HostConfigKey.AuthenticationRequired) ?? file.AuthenticationRequired, defaults.AuthenticationRequired, HostConfigKey.AuthenticationRequired),
            LogLevel = ParseLogLevel(Env(HostConfigKey.LogLevel) ?? file.LogLevel ?? defaults.LogLevel),
            DatabaseType = ParseEnum(Env(HostConfigKey.DatabaseType) ?? file.Database?.Type, defaults.DatabaseType, HostConfigKey.DatabaseType),
            DatabaseConnectionString = Env(HostConfigKey.DatabaseConnectionString) ?? file.Database?.ConnectionString ?? defaults.DatabaseConnectionString,
            AllowScriptSources = ParseBool(Env(HostConfigKey.AllowScriptSources), HostConfigKey.AllowScriptSources) ?? file.AllowScriptSources ?? defaults.AllowScriptSources,
            InstanceName = Env(HostConfigKey.InstanceName) ?? NullIfBlank(file.InstanceName) ?? defaults.InstanceName,
        };
    }

    internal static string NormalizeUrlBase(string urlBase)
    {
        var trimmed = urlBase.Trim().Trim('/');

        return trimmed.Length == 0 ? string.Empty : "/" + trimmed;
    }

    private static string ParseLogLevel(string value)
    {
        var level = value.Trim().ToLowerInvariant();

        return level is "info" or "debug" or "trace"
            ? level
            : throw new InvalidConfigurationException(HostConfigKey.LogLevel, value, "info, debug, trace");
    }

    private static int ParseInt(string value, HostConfigKey key)
    {
        return int.TryParse(value, out var result)
            ? result
            : throw new InvalidConfigurationException(key, value, "an integer");
    }

    private static bool? ParseBool(string? value, HostConfigKey key)
    {
        if (value is null)
        {
            return null;
        }

        return bool.TryParse(value, out var result)
            ? result
            : throw new InvalidConfigurationException(key, value, "true, false");
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback, HostConfigKey key)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var result) && Enum.IsDefined(result)
            ? result
            : throw new InvalidConfigurationException(key, value, string.Join(", ", Enum.GetNames<TEnum>()));
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Collects the <c>GENERRAL__*</c> overrides from configuration (environment variables, and in tests
    /// or on the command line the equivalent <c>GENERRAL:*</c> keys).
    /// </summary>
    public static Dictionary<string, string> ReadOverrides(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in HostConfigKey.All)
        {
            var value = configuration[key.EnvironmentVariable.Replace("__", ":", StringComparison.Ordinal)];

            if (!string.IsNullOrWhiteSpace(value))
            {
                result[key.EnvironmentVariable] = value;
            }
        }

        return result;
    }

    private HostConfigFile Load()
    {
        if (!File.Exists(_folders.ConfigFile))
        {
            return new HostConfigFile();
        }

        try
        {
            return HostConfigFile.Parse(File.ReadAllText(_folders.ConfigFile));
        }
        catch (YamlDotNet.Core.YamlException e)
        {
            throw new InvalidConfigurationException($"{_folders.ConfigFile} is not valid YAML: {e.Message}", e);
        }
    }

    private void EnsureApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_file.ApiKey) || IsOverriddenByEnvironment(HostConfigKey.ApiKey))
        {
            if (!File.Exists(_folders.ConfigFile))
            {
                Save();
            }

            return;
        }

        _file.ApiKey = GenerateApiKey();
        Save();
        GeneratedApiKey = true;
    }

    private void Save()
    {
        var temp = _folders.ConfigFile + ".tmp";
        File.WriteAllText(temp, _file.Serialize());
        File.Move(temp, _folders.ConfigFile, overwrite: true);
    }
}

public sealed class InvalidConfigurationException : Exception
{
    public InvalidConfigurationException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    public InvalidConfigurationException(HostConfigKey key, string value, string expected)
        : base($"Invalid value '{value}' for {key.EnvironmentVariable} / {key.YamlKey}; expected {expected}")
    {
    }
}
