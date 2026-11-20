// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Adapted from Sonarr src/NzbDrone.Core/Authentication/InitialUserService.cs
using Generral.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Authentication;

/// <summary>
/// Makes sure Forms authentication never starts without a user: on first start it creates
/// <c>GENERRAL__AUTH__USERNAME</c> / <c>GENERRAL__AUTH__PASSWORD</c>, or <c>generral</c> with a
/// random password printed to the log.
/// </summary>
public class InitialUserService(IUserService users, IHostConfigProvider config, IConfiguration configuration, ILogger<InitialUserService> logger)
{
    public const string DefaultUsername = "generral";
    public const string UsernameEnvironmentVariable = "GENERRAL__AUTH__USERNAME";
    public const string PasswordEnvironmentVariable = "GENERRAL__AUTH__PASSWORD";
    private const int GeneratedPasswordLength = 20;

    public void EnsureInitialUser()
    {
        if (users.FindUser() != null || config.Current.AuthenticationMethod != AuthenticationType.Forms)
        {
            return;
        }

        var username = configuration[UsernameEnvironmentVariable.Replace("__", ":", StringComparison.Ordinal)];
        var password = configuration[PasswordEnvironmentVariable.Replace("__", ":", StringComparison.Ordinal)];

        username = string.IsNullOrWhiteSpace(username) ? DefaultUsername : username;
        var isGenerated = string.IsNullOrWhiteSpace(password);

        if (isGenerated)
        {
            password = SecretGenerator.Generate(GeneratedPasswordLength);
        }

        users.Upsert(username, password);

        if (isGenerated)
        {
            logger.LogWarning("No user existed, created '{Username}' with the password: {Password}", username, password);
            logger.LogWarning("Change this password from Settings > General once you have signed in");
        }
        else
        {
            logger.LogInformation("No user existed, created '{Username}' with the configured password", username);
        }
    }
}
