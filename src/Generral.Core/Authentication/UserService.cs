// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Authentication/UserService.cs
// (PBKDF2-HMACSHA512 hashing; the legacy unsalted SHA256 upgrade path was dropped).
using System.Security.Cryptography;
using System.Text;
using Generral.Core.Datastore;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Authentication;

public interface IUserService
{
    User? FindUser();

    User? FindUser(string username, string password);

    User? FindUser(Guid identifier);

    User Upsert(string username, string? password);
}

public class UserService(GenerralDbContext db) : IUserService
{
    internal const int Iterations = 220000;
    private const int SaltSize = 128 / 8;
    private const int NumberOfBytes = 256 / 8;

    public User? FindUser()
    {
        return db.Users.AsNoTracking().OrderBy(u => u.Id).FirstOrDefault();
    }

    public User? FindUser(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var normalized = username.Trim().ToLowerInvariant();
        var user = db.Users.FirstOrDefault(u => u.Username == normalized);

        if (user == null || !VerifyHashedPassword(user, password))
        {
            return null;
        }

        if (user.Iterations < Iterations)
        {
            SetHashedPassword(user, password);
            db.SaveChanges();
        }

        return user;
    }

    public User? FindUser(Guid identifier)
    {
        return db.Users.AsNoTracking().FirstOrDefault(u => u.Identifier == identifier);
    }

    public User Upsert(string username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required", nameof(username));
        }

        var user = db.Users.OrderBy(u => u.Id).FirstOrDefault();

        if (user == null)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password is required", nameof(password));
            }

            user = new User
            {
                Identifier = Guid.NewGuid(),
                Username = username.Trim().ToLowerInvariant(),
                PasswordHash = string.Empty,
                Salt = string.Empty,
            };

            SetHashedPassword(user, password);
            db.Users.Add(user);
        }
        else
        {
            user.Username = username.Trim().ToLowerInvariant();

            if (!string.IsNullOrEmpty(password))
            {
                SetHashedPassword(user, password);

                // Invalidate existing sessions, which are bound to the identifier.
                user.Identifier = Guid.NewGuid();
            }
        }

        db.SaveChanges();

        return user;
    }

    private static void SetHashedPassword(User user, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        user.Iterations = Iterations;
        user.Salt = Convert.ToBase64String(salt);
        user.PasswordHash = GetHashedPassword(password, salt, Iterations);
    }

    private static string GetHashedPassword(string password, byte[] salt, int iterations)
    {
        return Convert.ToBase64String(KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA512,
            iterationCount: iterations,
            numBytesRequested: NumberOfBytes));
    }

    private static bool VerifyHashedPassword(User user, string password)
    {
        var salt = Convert.FromBase64String(user.Salt);
        var hashed = GetHashedPassword(password, salt, user.Iterations);

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(user.PasswordHash), Encoding.UTF8.GetBytes(hashed));
    }
}
