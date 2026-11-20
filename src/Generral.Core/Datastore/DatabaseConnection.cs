using Generral.Core.Configuration;
using Microsoft.Data.Sqlite;

namespace Generral.Core.Datastore;

public static class DatabaseConnection
{
    public const string DefaultFileName = "generral.db";

    /// <summary>
    /// Builds the SQLite connection string. The configured value may be a full connection string
    /// (<c>Data Source=…;…</c>) or just a path to the database file; empty means <c>&lt;config&gt;/generral.db</c>.
    /// </summary>
    public static string GetConnectionString(HostSettings settings, IAppFolders folders)
    {
        var configured = settings.DatabaseConnectionString.Trim();

        var builder = configured.Contains('=')
            ? new SqliteConnectionStringBuilder(configured)
            : new SqliteConnectionStringBuilder
            {
                DataSource = configured.Length == 0 ? Path.Combine(folders.ConfigFolder, DefaultFileName) : configured,
            };

        builder.Cache = SqliteCacheMode.Shared;
        builder.Pooling = true;

        return builder.ToString();
    }
}
