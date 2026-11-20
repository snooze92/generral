// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Common/SecretGenerator.cs
using System.Security.Cryptography;

namespace Generral.Core.Authentication;

public static class SecretGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string Generate(int length)
    {
        return RandomNumberGenerator.GetString(Alphabet, length);
    }
}
