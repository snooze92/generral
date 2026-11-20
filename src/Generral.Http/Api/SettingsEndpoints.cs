using Generral.Core.Authentication;
using Generral.Core.Configuration;
using Generral.Core.Instrumentation;
using Microsoft.AspNetCore.Routing;

namespace Generral.Http.Api;

/// <summary>Settings → General. Mirrors the subset of Sonarr's GeneralSettingsResource that Generral supports.</summary>
public sealed record GeneralSettingsResource
{
    public string BindAddress { get; init; } = "*";

    public int Port { get; init; }

    public string UrlBase { get; init; } = string.Empty;

    public string InstanceName { get; init; } = string.Empty;

    public string AuthenticationMethod { get; init; } = "forms";

    public string AuthenticationRequired { get; init; } = "enabled";

    public string Username { get; init; } = string.Empty;

    /// <summary>Write-only. Empty means "keep the current password".</summary>
    public string Password { get; init; } = string.Empty;

    public string PasswordConfirmation { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public string LogLevel { get; init; } = "info";

    public bool AllowScriptSources { get; init; }

    /// <summary>Generral-specific: fields fixed by GENERRAL__* environment variables (read-only in the UI).</summary>
    public IReadOnlyList<string> OverriddenByEnvironment { get; init; } = [];
}

public static class SettingsEndpoints
{
    private static readonly (string Property, HostConfigKey Key)[] OverridableFields =
    [
        ("bindAddress", HostConfigKey.BindAddress),
        ("port", HostConfigKey.Port),
        ("urlBase", HostConfigKey.UrlBase),
        ("instanceName", HostConfigKey.InstanceName),
        ("authenticationMethod", HostConfigKey.AuthenticationMethod),
        ("authenticationRequired", HostConfigKey.AuthenticationRequired),
        ("apiKey", HostConfigKey.ApiKey),
        ("logLevel", HostConfigKey.LogLevel),
        ("allowScriptSources", HostConfigKey.AllowScriptSources),
    ];

    public static void MapSettingsEndpoints(this IEndpointRouteBuilder api)
    {
        var settings = api.MapGroup("/settings");

        settings.MapGet("/general", (IHostConfigProvider config, IUserService users) => ToResource(config, users));
        settings.MapPut("/general", SaveGeneral);
        settings.MapPost("/general/resetapikey", ResetApiKey);

        settings.MapGet("/ui", (IUiSettingsService ui) => ui.Get());
        settings.MapPut("/ui", SaveUi);
    }

    private static GeneralSettingsResource ToResource(IHostConfigProvider config, IUserService users)
    {
        var current = config.Current;

        return new GeneralSettingsResource
        {
            BindAddress = current.BindAddress,
            Port = current.Port,
            UrlBase = current.UrlBase,
            InstanceName = current.InstanceName,
            AuthenticationMethod = JsonNames.Of(current.AuthenticationMethod),
            AuthenticationRequired = JsonNames.Of(current.AuthenticationRequired),
            Username = users.FindUser()?.Username ?? string.Empty,
            ApiKey = current.ApiKey,
            LogLevel = current.LogLevel,
            AllowScriptSources = current.AllowScriptSources,
            OverriddenByEnvironment = OverridableFields
                .Where(f => config.IsOverriddenByEnvironment(f.Key))
                .Select(f => f.Property)
                .ToList(),
        };
    }

    private static IResult SaveGeneral(GeneralSettingsResource resource, IHostConfigProvider config, IUserService users)
    {
        var failures = new List<ValidationFailure>();

        if (resource.Port is < 1 or > 65535)
        {
            failures.Add(new ValidationFailure("port", "Port must be between 1 and 65535"));
        }

        if (!Enum.TryParse<AuthenticationType>(resource.AuthenticationMethod, true, out var method) || !Enum.IsDefined(method))
        {
            failures.Add(new ValidationFailure("authenticationMethod", "Unsupported authentication method"));
        }

        if (!Enum.TryParse<AuthenticationRequiredType>(resource.AuthenticationRequired, true, out var required) || !Enum.IsDefined(required))
        {
            failures.Add(new ValidationFailure("authenticationRequired", "Unsupported value"));
        }

        if (resource.LogLevel is not ("info" or "debug" or "trace"))
        {
            failures.Add(new ValidationFailure("logLevel", "Log level must be info, debug or trace"));
        }

        var existingUser = users.FindUser();
        var changesUser = !string.IsNullOrEmpty(resource.Password) ||
                          (existingUser != null && !string.Equals(existingUser.Username, resource.Username.Trim(), StringComparison.OrdinalIgnoreCase));

        if (method == AuthenticationType.Forms || changesUser)
        {
            if (string.IsNullOrWhiteSpace(resource.Username))
            {
                failures.Add(new ValidationFailure("username", "Username is required"));
            }

            if (existingUser == null && string.IsNullOrEmpty(resource.Password))
            {
                failures.Add(new ValidationFailure("password", "Password is required"));
            }

            if (resource.Password != resource.PasswordConfirmation)
            {
                failures.Add(new ValidationFailure("passwordConfirmation", "Must match Password"));
            }
        }

        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        // Fields fixed by the environment are ignored rather than rejected, so the UI can send the whole resource.
        string? Unless(HostConfigKey key, string value) => config.IsOverriddenByEnvironment(key) ? null : value;

        config.Update(new HostSettingsUpdate
        {
            BindAddress = Unless(HostConfigKey.BindAddress, resource.BindAddress),
            Port = config.IsOverriddenByEnvironment(HostConfigKey.Port) ? null : resource.Port,
            UrlBase = Unless(HostConfigKey.UrlBase, resource.UrlBase),
            InstanceName = Unless(HostConfigKey.InstanceName, resource.InstanceName),
            AuthenticationMethod = config.IsOverriddenByEnvironment(HostConfigKey.AuthenticationMethod) ? null : method,
            AuthenticationRequired = config.IsOverriddenByEnvironment(HostConfigKey.AuthenticationRequired) ? null : required,
            LogLevel = Unless(HostConfigKey.LogLevel, resource.LogLevel),
        });

        Logging.LevelSwitch.MinimumLevel = Logging.ToLevel(config.Current.LogLevel);

        if (changesUser)
        {
            users.Upsert(resource.Username, string.IsNullOrEmpty(resource.Password) ? null : resource.Password);
        }

        return Results.Ok(ToResource(config, users));
    }

    private static IResult ResetApiKey(IHostConfigProvider config, IUserService users)
    {
        if (config.IsOverriddenByEnvironment(HostConfigKey.ApiKey))
        {
            return ValidationFailure.BadRequest(new ValidationFailure("apiKey", $"The API key is set by {HostConfigKey.ApiKey.EnvironmentVariable}"));
        }

        config.ResetApiKey();

        return Results.Ok(ToResource(config, users));
    }

    private static IResult SaveUi(UiSettings settings, IUiSettingsService ui)
    {
        try
        {
            return Results.Ok(ui.Save(settings));
        }
        catch (ArgumentException e)
        {
            return ValidationFailure.BadRequest(new ValidationFailure(string.Empty, e.Message));
        }
    }
}
