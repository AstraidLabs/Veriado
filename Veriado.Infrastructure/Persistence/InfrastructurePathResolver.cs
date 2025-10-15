namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides helper methods to resolve consistent database paths for SQLite connections.
/// </summary>
internal static class InfrastructurePathResolver
{
    /// <summary>
    /// Resolves the database path using the configured path or the default application location.
    /// </summary>
    /// <param name="configuredPath">The configured database path.</param>
    /// <returns>The resolved absolute database path.</returns>
    public static string ResolveDatabasePath(string? configuredPath)
    {
        var dbPath = !string.IsNullOrWhiteSpace(configuredPath)
            ? configuredPath
            : GetDefaultDatabasePath();

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return dbPath;
    }

    /// <summary>
    /// Builds a SQLite connection string for the supplied database path.
    /// </summary>
    /// <param name="dbPath">The absolute database path.</param>
    /// <returns>The SQLite connection string.</returns>
    public static string BuildConnectionString(string dbPath)
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Cache = SqliteCacheMode.Shared,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };

        return connectionStringBuilder.ConnectionString;
    }

    /// <summary>
    /// Gets the default database path under the user's local application data directory.
    /// </summary>
    /// <returns>The absolute default database path.</returns>
    public static string GetDefaultDatabasePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "Veriado", "veriado.db");
        }

        return Path.Combine(AppContext.BaseDirectory, "veriado-data", "veriado.db");
    }
}
