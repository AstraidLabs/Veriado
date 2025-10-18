using Microsoft.Data.Sqlite;

namespace Veriado.Infrastructure.Persistence.Connections;

/// <summary>
/// Provides the canonical SQLite connection information used by the infrastructure layer.
/// </summary>
public interface IConnectionStringProvider
{
    /// <summary>
    /// Gets the absolute path to the SQLite database file.
    /// </summary>
    string DatabasePath { get; }

    /// <summary>
    /// Gets the SQLite connection string.
    /// </summary>
    string ConnectionString { get; }

    /// <summary>
    /// Creates a new <see cref="SqliteConnection"/> configured with the canonical connection string.
    /// </summary>
    /// <returns>A new unopened <see cref="SqliteConnection"/> instance.</returns>
    SqliteConnection CreateConnection();
}
