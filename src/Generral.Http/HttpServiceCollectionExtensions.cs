using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Generral.Core.Authentication;
using Generral.Core.Configuration;
using Generral.Core.Lifecycle;
using Generral.Http.Api;
using Generral.Http.Authentication;
using Generral.Http.Frontend;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

namespace Generral.Http;

public static partial class HttpServiceCollectionExtensions
{
    public static IServiceCollection AddGenerralHttp(this IServiceCollection services, IAppFolders folders, IHostConfigProvider config)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

        services.AddDataProtection()
            .SetApplicationName("Generral")
            .PersistKeysToFileSystem(new DirectoryInfo(folders.DataProtectionFolder));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<IRestartService, RestartService>();

        var cookieName = CookieNameRegex().Replace(config.Current.InstanceName, string.Empty) + "Auth";

        services.AddAuthentication(AuthSchemes.Forms)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, _ => { })
            .AddScheme<AuthenticationSchemeOptions, ExternalAuthenticationHandler>(AuthSchemes.External, _ => { })
            .AddCookie(AuthSchemes.Forms, options =>
            {
                options.Cookie.Name = cookieName.Length > 4 ? cookieName : "GenerralAuth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = RedirectToLogin;
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;

                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = ValidateUser;
            });

        services.AddSingleton<IAuthorizationPolicyProvider, UiAuthorizationPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, LocalAddressBypassHandler>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthSchemes.ApiKeyPolicy, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.ApiKey)
                .RequireAuthenticatedUser());

            // Everything not explicitly opened up needs at least an API key.
            options.FallbackPolicy = new AuthorizationPolicyBuilder(AuthSchemes.ApiKey)
                .RequireAuthenticatedUser()
                .Build();
        });

        services.AddOpenApi("v1");

        return services;
    }

    public static WebApplication UseGenerralHttp(this WebApplication app)
    {
        var urlBase = app.Services.GetRequiredService<IHostConfigProvider>().Current.UrlBase;

        if (urlBase.Length > 0)
        {
            app.UsePathBase(urlBase);

            // Requests outside the URL base (other than /ping, used by healthchecks) are not ours.
            app.Use(async (context, next) =>
            {
                if (!context.Request.PathBase.HasValue && !context.Request.Path.StartsWithSegments("/ping"))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;

                    return;
                }

                await next();
            });
        }

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Application-Version"] = BuildInfo.Version;
            await next();
        });

        app.UseFrontendStaticFiles();

        // Explicit, so routing sees the path with the URL base already stripped.
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/ping", () => Results.Json(new { status = "OK" })).AllowAnonymous();
        app.MapMethods("/ping", ["HEAD"], () => Results.Ok()).AllowAnonymous();

        app.MapAuthEndpoints();

        // UI requests authenticate with either the API key (initialize.json hands it to the SPA) or the UI method.
        var api = app.MapGroup("/api/v1").RequireAuthorization(AuthSchemes.UiPolicy);
        api.MapSystemEndpoints();
        api.MapSettingsEndpoints();
        api.MapSourceEndpoints();
        api.MapTagEndpoints();
        api.MapCommandEndpoints();
        api.MapOpenApi("/openapi.json");

        app.MapFrontend();

        return app;
    }

    private static Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        var request = context.Request;
        var isApi = request.Path.StartsWithSegments("/api") ||
                    request.Path.StartsWithSegments("/initialize.json") ||
                    string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.Ordinal) ||
                    request.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);

        if (isApi)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return Task.CompletedTask;
        }

        var returnUrl = request.PathBase + request.Path + request.QueryString;
        context.Response.Redirect($"{request.PathBase}/login?returnUrl={Uri.EscapeDataString(returnUrl)}");

        return Task.CompletedTask;
    }

    /// <summary>Reject cookies whose user was deleted or whose password changed (new identifier).</summary>
    private static Task ValidateUser(CookieValidatePrincipalContext context)
    {
        var identifier = context.Principal?.FindFirst(AuthEndpoints.IdentifierClaim)?.Value;
        var users = context.HttpContext.RequestServices.GetRequiredService<IUserService>();

        if (!Guid.TryParse(identifier, out var id) || users.FindUser(id) == null)
        {
            context.RejectPrincipal();

            return context.HttpContext.SignOutAsync(AuthSchemes.Forms);
        }

        return Task.CompletedTask;
    }

    [GeneratedRegex("[^a-zA-Z0-9]+")]
    private static partial Regex CookieNameRegex();
}
