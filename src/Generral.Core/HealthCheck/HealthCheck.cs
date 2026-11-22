// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/HealthCheck/HealthCheck.cs, HealthCheckBase.cs and IProvideHealthCheck.cs
namespace Generral.Core.HealthCheck;

public enum HealthCheckResult
{
    Ok,
    Notice,
    Warning,
    Error,
}

/// <summary>The result of one check; <see cref="Source"/> is the check's class name, as in Sonarr.</summary>
public sealed record HealthCheck(string Source, HealthCheckResult Type, string Message)
{
    public static HealthCheck Ok(IProvideHealthCheck check) => new(check.GetType().Name, HealthCheckResult.Ok, "");

    public static HealthCheck Warning(IProvideHealthCheck check, string message) => new(check.GetType().Name, HealthCheckResult.Warning, message);

    public static HealthCheck Error(IProvideHealthCheck check, string message) => new(check.GetType().Name, HealthCheckResult.Error, message);
}

/// <summary>A health check, resolved from a fresh scope on every run.</summary>
public interface IProvideHealthCheck
{
    Task<HealthCheck> CheckAsync(CancellationToken cancellationToken);
}
