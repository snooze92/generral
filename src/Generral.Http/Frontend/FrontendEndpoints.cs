using Generral.Core.Configuration;
using Generral.Core.Lifecycle;
using Generral.Http.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace Generral.Http.Frontend;

/// <summary>
/// Serves the built SPA: hashed assets, <c>index.html</c> for client-side routes, the standalone
/// <c>login.html</c>, and <c>initialize.json</c> (Sonarr's bootstrap document).
/// </summary>
public static class FrontendEndpoints
{
    private const string UrlBasePlaceholder = "__URL_BASE__";
    private const string ThemePlaceholder = "__THEME__";
    private const string InstanceNamePlaceholder = "__INSTANCE_NAME__";

    private static readonly string[] ReservedPrefixes = ["/api", "/feed", "/signalr", "/ping", "/login", "/logout", "/initialize.json"];

    /// <summary>
    /// Static assets are public (the login page needs them, and they hold no secrets), so this runs before
    /// authentication.
    /// </summary>
    public static void UseFrontendStaticFiles(this WebApplication app)
    {
        var uiRoot = app.Services.GetRequiredService<IAppFolders>().UiFolder;

        if (!File.Exists(Path.Combine(uiRoot, "index.html")))
        {
            app.Logger.LogWarning("No UI found in {UiFolder}; only the API is available (build the frontend, or set GENERRAL__UIDIR)", uiRoot);

            return;
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(uiRoot),
            ContentTypeProvider = new FileExtensionContentTypeProvider(),
            OnPrepareResponse = ctx =>
            {
                // Vite emits content-hashed file names under /assets.
                ctx.Context.Response.Headers.CacheControl = ctx.Context.Request.Path.StartsWithSegments("/assets")
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
            },
        });
    }

    public static void MapFrontend(this WebApplication app)
    {
        var uiRoot = app.Services.GetRequiredService<IAppFolders>().UiFolder;

        app.MapGet("/login", (HttpContext context, IHostConfigProvider config, IUiSettingsService ui) =>
                Html(context, Path.Combine(uiRoot, "login.html"), config, ui))
            .AllowAnonymous();

        app.MapGet("/initialize.json", (IHostConfigProvider config, IUiSettingsService ui) =>
            {
                var settings = config.Current;

                return Results.Json(new
                {
                    apiRoot = settings.UrlBase + "/api/v1",
                    apiKey = settings.ApiKey,
                    version = BuildInfo.Version,
                    instanceName = settings.InstanceName,
                    theme = ui.Get().Theme,
                    urlBase = settings.UrlBase,
                    isProduction = !BuildInfo.IsDebug,
                });
            })
            .RequireAuthorization(AuthSchemes.UiPolicy);

        app.MapFallback((HttpContext context, IHostConfigProvider config, IUiSettingsService ui) =>
            {
                var path = context.Request.Path.Value ?? "/";

                // Unknown API routes and missing files are real 404s; everything else is a client-side route.
                if (ReservedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) || Path.HasExtension(path))
                {
                    return Results.NotFound();
                }

                return Html(context, Path.Combine(uiRoot, "index.html"), config, ui);
            })
            .RequireAuthorization(AuthSchemes.UiPolicy);
    }

    private static IResult Html(HttpContext context, string file, IHostConfigProvider config, IUiSettingsService ui)
    {
        if (!File.Exists(file))
        {
            return Results.NotFound();
        }

        var settings = config.Current;
        var html = File.ReadAllText(file)
            .Replace(UrlBasePlaceholder, settings.UrlBase, StringComparison.Ordinal)
            .Replace(ThemePlaceholder, ui.Get().Theme, StringComparison.Ordinal)
            .Replace(InstanceNamePlaceholder, System.Net.WebUtility.HtmlEncode(settings.InstanceName), StringComparison.Ordinal);

        // Absolute asset URLs ("/assets/…", "/Content/…", "/favicon.svg") need the URL base when running behind a sub-path.
        if (settings.UrlBase.Length > 0)
        {
            html = html
                .Replace("=\"/assets/", $"=\"{settings.UrlBase}/assets/", StringComparison.Ordinal)
                .Replace("=\"/Content/", $"=\"{settings.UrlBase}/Content/", StringComparison.Ordinal)
                .Replace("=\"/favicon.", $"=\"{settings.UrlBase}/favicon.", StringComparison.Ordinal);
        }

        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";

        return Results.Content(html, "text/html; charset=utf-8");
    }
}
