// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Adapted from Sonarr@51a2412af235 src/Sonarr.Http/Authentication/UiAuthorizationPolicyProvider.cs,
// UiAuthorizationHandler.cs and BypassableDenyAnonymousAuthorizationRequirement.cs
using System.Net;
using Generral.Core.Configuration;
using Generral.Core.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;

namespace Generral.Http.Authentication;

/// <summary>Deny anonymous users, unless <see cref="LocalAddressBypassHandler"/> lets them through.</summary>
public class BypassableDenyAnonymousRequirement : DenyAnonymousAuthorizationRequirement;

/// <summary>
/// Builds the <c>UI</c> policy (API key, or the configured UI method) at request time, so changing the authentication method in
/// Settings → General takes effect without a restart.
/// </summary>
public class UiAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options, IHostConfigProvider config)
    : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.Equals(AuthSchemes.UiPolicy, StringComparison.OrdinalIgnoreCase))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        var scheme = config.Current.AuthenticationMethod switch
        {
            AuthenticationType.External => AuthSchemes.External,
            _ => AuthSchemes.Forms,
        };

        // API key first: when both challenge, the UI scheme's response (login redirect / 401) wins.
        var policy = new AuthorizationPolicyBuilder(AuthSchemes.ApiKey, scheme)
            .AddRequirements(new BypassableDenyAnonymousRequirement())
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

/// <summary>"Authentication required: Disabled for local addresses".</summary>
public class LocalAddressBypassHandler(IHostConfigProvider config) : AuthorizationHandler<BypassableDenyAnonymousRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, BypassableDenyAnonymousRequirement requirement)
    {
        if (config.Current.AuthenticationRequired == AuthenticationRequiredType.DisabledForLocalAddresses &&
            context.Resource is HttpContext httpContext &&
            IsLocalRequest(httpContext))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    internal static bool IsLocalRequest(HttpContext context)
    {
        // Behind a reverse proxy the peer is always local, so the real client is unknown: don't bypass.
        if (context.Request.Headers.ContainsKey("X-Forwarded-For"))
        {
            return false;
        }

        var address = context.Connection.RemoteIpAddress;

        return address != null && (address.IsLocalAddress() || IPAddress.IsLoopback(address));
    }
}
