// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Http/Authentication/ApiKeyAuthenticationHandler.cs
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Generral.Core.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Generral.Http.Authentication;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IHostConfigProvider config)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private string? ParseApiKey()
    {
        if (Request.Query.TryGetValue(AuthSchemes.ApiKeyQuery, out var query) && !string.IsNullOrWhiteSpace(query))
        {
            return query.FirstOrDefault();
        }

        // SignalR (later) passes the key as access_token on websocket connects.
        if (Request.Path.StartsWithSegments("/signalr") && Request.Query.TryGetValue("access_token", out var token))
        {
            return token.FirstOrDefault();
        }

        if (Request.Headers.TryGetValue(AuthSchemes.ApiKeyHeader, out var header))
        {
            return header.FirstOrDefault();
        }

        var authorization = Request.Headers.Authorization.FirstOrDefault();

        return authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authorization["Bearer ".Length..]
            : null;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var provided = ParseApiKey();

        if (string.IsNullOrWhiteSpace(provided))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var expected = config.Current.ApiKey;

        if (expected.Length == 0 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided.Trim())))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var identity = new ClaimsIdentity([new Claim("ApiKey", "true")], Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;

        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;

        return Task.CompletedTask;
    }
}
