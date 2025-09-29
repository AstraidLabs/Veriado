using System.Data;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides reusable helpers for configuring SQLite connections with required PRAGMA settings.
/// </summary>
internal static class SqlitePragmaHelper
{
    private const string CommandText = "PRAGMA journal_mode=WAL;PRAGMA synchronous=FULL;PRAGMA foreign_keys=ON;PRAGMA temp_store=MEMORY;PRAGMA mmap_size=134217728;PRAGMA cache_size=-32768;PRAGMA busy_timeout=5000";

    /// <summary>
    /// Applies the required PRAGMA statements to the specified SQLite connection.
    /// </summary>
    /// <param name="connection">The SQLite connection.</param>
    /// <param name="cancellationToken">A cancellation token to observe while applying PRAGMA statements.</param>
    public static async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = CommandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
