// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Backup/BackupService.cs, Backup.cs, BackupCommand.cs, MakeDatabaseBackup.cs
// and src/NzbDrone.Core/Datastore/RestoreDatabase (restore on next start)
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.Lifecycle;
using Generral.Core.Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Backup;

public enum BackupType
{
    Scheduled,
    Manual,
}

public sealed record BackupInfo(string Name, BackupType Type, long Size, DateTime Time, string Path)
{
    /// <summary>Stable id from type and name, as Sonarr's (a hash, so ids survive restarts).</summary>
    public int Id => (int)(StableHash($"backup-{Type}-{Name}") & 0x7FFFFFFF);

    private static uint StableHash(string value)
    {
        var hash = 2166136261;
        foreach (var c in value)
        {
            hash = (hash ^ c) * 16777619;
        }

        return hash;
    }
}

public class RestoreBackupFailedException(string message) : Exception(message);

/// <summary>
/// Zips the SQLite database and <c>config.yaml</c> into <c>&lt;config&gt;/Backups/{scheduled,manual}</c>. A restore stages the files
/// as <c>generral.restore</c> and <c>config.yaml</c>; the database is swapped in at the next start (Sonarr's behaviour).
/// </summary>
public sealed partial class BackupService(IServiceScopeFactory scopes, IAppFolders folders, TimeProvider time, ILogger<BackupService> logger)
{
    public const int DefaultRetentionDays = 28;
    public const string RestoreFileName = "generral.restore";

    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    public string BackupFolder => Path.Combine(folders.ConfigFolder, "Backups");

    public int RetentionDays { get; init; } = DefaultRetentionDays;

    public async Task<BackupInfo> BackupAsync(BackupType type, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(BackupFolder, type.ToString().ToLowerInvariant());
        Directory.CreateDirectory(folder);
        var now = time.GetLocalNow();
        var path = Path.Combine(folder, $"generral_backup_v{BuildInfo.Version}_{now:yyyy.MM.dd_HH.mm.ss}.zip");

        if (type == BackupType.Scheduled)
        {
            CleanupOldBackups(folder);
        }

        var databaseCopy = Path.Combine(Path.GetTempPath(), $"generral-backup-{Guid.NewGuid():N}.db");
        try
        {
            await CopyDatabaseAsync(databaseCopy, cancellationToken);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(databaseCopy, DatabaseConnection.DefaultFileName);
                if (File.Exists(folders.ConfigFile))
                {
                    zip.CreateEntryFromFile(folders.ConfigFile, Path.GetFileName(folders.ConfigFile));
                }

                await using var info = new StreamWriter(zip.CreateEntry("INFO").Open());
                await info.WriteAsync($"v{BuildInfo.Version}\n{now:yyyy-MM-dd HH:mm:ss}\n");
            }
        }
        finally
        {
            File.Delete(databaseCopy);
        }

        logger.LogInformation("Backup created: {Path}", path);
        return Describe(path, type);
    }

    public IReadOnlyList<BackupInfo> All() =>
        Enum.GetValues<BackupType>()
            .SelectMany(type =>
            {
                var folder = Path.Combine(BackupFolder, type.ToString().ToLowerInvariant());
                return Directory.Exists(folder)
                    ? Directory.EnumerateFiles(folder, "*.zip").Where(f => BackupFileRegex().IsMatch(Path.GetFileName(f))).Select(f => Describe(f, type))
                    : [];
            })
            .OrderByDescending(b => b.Time)
            .ToList();

    public BackupInfo? Find(int id) => All().FirstOrDefault(b => b.Id == id);

    public void Delete(BackupInfo backup) => File.Delete(backup.Path);

    /// <summary>Validates a backup zip and stages it; the caller must restart the application.</summary>
    /// <exception cref="RestoreBackupFailedException">Not a Generral backup.</exception>
    public void Restore(Stream zipStream)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException or IOException)
        {
            throw new RestoreBackupFailedException("The backup is not a valid zip file");
        }

        using (zip)
        {
            var database = zip.GetEntry(DatabaseConnection.DefaultFileName) ?? throw new RestoreBackupFailedException("Unable to restore database file from backup");
            var configEntry = zip.GetEntry(Path.GetFileName(folders.ConfigFile));

            var stagedDatabase = Path.Combine(folders.ConfigFolder, RestoreFileName + ".tmp");
            database.ExtractToFile(stagedDatabase, overwrite: true);
            if (!IsSqliteDatabase(stagedDatabase))
            {
                File.Delete(stagedDatabase);
                throw new RestoreBackupFailedException("Backup database is not a valid SQLite database");
            }

            string? configYaml = null;
            if (configEntry is not null)
            {
                using var reader = new StreamReader(configEntry.Open());
                configYaml = reader.ReadToEnd();
                try
                {
                    HostConfigFile.Parse(configYaml);
                }
                catch (YamlDotNet.Core.YamlException)
                {
                    File.Delete(stagedDatabase);
                    throw new RestoreBackupFailedException("Backup config file is not a valid config file");
                }
            }

            File.Move(stagedDatabase, Path.Combine(folders.ConfigFolder, RestoreFileName), overwrite: true);
            if (configYaml is not null)
            {
                File.WriteAllText(folders.ConfigFile, configYaml);
            }
        }

        logger.LogInformation("Backup staged for restore; it is applied at the next start");
    }

    /// <summary>At startup, before migrations: swaps a staged <c>generral.restore</c> in for the database.</summary>
    public static void ApplyPendingRestore(IAppFolders folders, HostSettings settings, ILogger logger)
    {
        var restore = Path.Combine(folders.ConfigFolder, RestoreFileName);
        if (!File.Exists(restore))
        {
            return;
        }

        var database = new SqliteConnectionStringBuilder(DatabaseConnection.GetConnectionString(settings, folders)).DataSource;
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
        {
            File.Delete(database + suffix);
        }

        File.Move(restore, database, overwrite: true);
        logger.LogInformation("Restored the database from a backup");
    }

    private async Task CopyDatabaseAsync(string target, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var source = (SqliteConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
            await destination.OpenAsync(cancellationToken);
            source.BackupDatabase(destination);
            await using var command = destination.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=DELETE;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private void CleanupOldBackups(string folder)
    {
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(folder, "*.zip").Where(f => BackupFileRegex().IsMatch(Path.GetFileName(f))))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                logger.LogDebug("Deleting old backup file: {File}", file);
                File.Delete(file);
            }
        }
    }

    private static BackupInfo Describe(string path, BackupType type)
    {
        var file = new FileInfo(path);
        return new BackupInfo(file.Name, type, file.Length, file.LastWriteTimeUtc, path);
    }

    private static bool IsSqliteDatabase(string file)
    {
        var header = new byte[SqliteHeader.Length];
        using var stream = File.OpenRead(file);
        return stream.Read(header, 0, header.Length) == header.Length && header.SequenceEqual(SqliteHeader);
    }

    [GeneratedRegex(@"^generral_backup_(v[0-9A-Za-z.\-]+_)?[._0-9]+\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex BackupFileRegex();
}

/// <summary><c>Backup { type? }</c>: scheduled backups prune old ones (retention), manual ones don't.</summary>
public sealed class BackupHandler(BackupService backups) : ICommandHandler
{
    public const string CommandName = "Backup";

    public string Name => CommandName;

    public async Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken)
    {
        var type = context.Trigger == CommandTrigger.Scheduled ? BackupType.Scheduled : BackupType.Manual;
        var backup = await backups.BackupAsync(type, cancellationToken);
        return $"Backup created: {backup.Name}";
    }
}
