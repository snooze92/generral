using System.Security.Claims;
using Generral.Core.Authentication;
using Generral.Core.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Generral.Http.Authentication;

public static class AuthEndpoints
{
    public const string IdentifierClaim = "identifier";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/login", Login).AllowAnonymous().DisableAntiforgery();
        app.MapGet("/logout", Logout).AllowAnonymous();
    }

    private static async Task<IResult> Login(
        HttpContext context,
        IUserService users,
        IHostConfigProvider config,
        LoginThrottle throttle,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Auth");
        var urlBase = config.Current.UrlBase;
        var form = await context.Request.ReadFormAsync();
        var returnUrl = SafeReturnUrl(context.Request.Query["returnUrl"].FirstOrDefault(), urlBase);
        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        string FailedRedirect(DateTimeOffset? lockoutUntil)
        {
            var url = $"{urlBase}/login?loginFailed=true&returnUrl={Uri.EscapeDataString(returnUrl)}";

            return lockoutUntil is { } until ? $"{url}&lockoutUntil={until.ToUnixTimeMilliseconds()}" : url;
        }

        if (throttle.GetLockoutEnd(client) is { } lockedUntil)
        {
            return Results.Redirect(FailedRedirect(lockedUntil));
        }

        var user = users.FindUser(form["username"].ToString(), form["password"].ToString());

        if (user == null)
        {
            throttle.RecordFailure(client);
            logger.LogWarning("Auth-Failure ip {Ip} username '{Username}'", client, form["username"].ToString());

            return Results.Redirect(FailedRedirect(throttle.GetLockoutEnd(client)));
        }

        throttle.RecordSuccess(client);

        var identity = new ClaimsIdentity(
            [new Claim("user", user.Username), new Claim(IdentifierClaim, user.Identifier.ToString())],
            AuthSchemes.Forms,
            "user",
            null);

        await context.SignInAsync(
            AuthSchemes.Forms,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = form["rememberMe"] == "on" || form["rememberMe"] == "true" });

        logger.LogInformation("Auth-Success ip {Ip} username '{Username}'", client, user.Username);

        return Results.Redirect(returnUrl);
    }

    private static async Task<IResult> Logout(HttpContext context, IHostConfigProvider config)
    {
        await context.SignOutAsync(AuthSchemes.Forms);

        return Results.Redirect(config.Current.UrlBase + "/");
    }

    /// <summary>Only allow local, rooted return URLs (no open redirects).</summary>
    internal static string SafeReturnUrl(string? returnUrl, string urlBase)
    {
        var fallback = urlBase + "/";

        if (string.IsNullOrWhiteSpace(returnUrl) ||
            !returnUrl.StartsWith('/') ||
            returnUrl.StartsWith("//", StringComparison.Ordinal) ||
            returnUrl.StartsWith("/\\", StringComparison.Ordinal) ||
            returnUrl.Contains("://", StringComparison.Ordinal))
        {
            return fallback;
        }

        if (urlBase.Length > 0 && !returnUrl.StartsWith(urlBase + "/", StringComparison.OrdinalIgnoreCase) && !returnUrl.Equals(urlBase, StringComparison.OrdinalIgnoreCase))
        {
            return urlBase + returnUrl;
        }

        return returnUrl;
    }
}
