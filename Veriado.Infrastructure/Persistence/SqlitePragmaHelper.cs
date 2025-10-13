using System.Data;
using Microsoft.Extensions.Logging;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides reusable helpers for configuring SQLite connections with required PRAGMA settings.
/// </summary>
internal static class SqlitePragmaHelper
{
    internal const string RequiredJournalMode = "wal";
    internal const string RequiredSynchronous = "normal";
    internal const int RequiredBusyTimeoutMs = 8000;

    /// <summary>
    /// Applies the required PRAGMA statements to the specified SQLite connection.
    /// </summary>
    /// <param name="connection">The SQLite connection.</param>
    /// <param name="logger">Optional logger used to emit telemetry about the PRAGMA application.</param>
    /// <param name="cancellationToken">A cancellation token to observe while applying PRAGMA statements.</param>
    public static async Task ApplyAsync(SqliteConnection connection, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        logger?.LogInformation("Applying SQLite PRAGMA settings to {DataSource}", connection.DataSource ?? "memory");

        await EnsureJournalModeAsync(connection, logger, cancellationToken).ConfigureAwait(false);
        await EnsureSynchronousAsync(connection, logger, cancellationToken).ConfigureAwait(false);
        await EnsureBusyTimeoutAsync(connection, logger, cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA temp_store=MEMORY;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA page_size=4096;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA mmap_size=268435456;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA cache_size=-32768;", cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureJournalModeAsync(SqliteConnection connection, ILogger? logger, CancellationToken cancellationToken)
    {
        var current = await ExecuteScalarAsync(connection, "PRAGMA journal_mode;", cancellationToken).ConfigureAwait(false);
        if (string.Equals(current, RequiredJournalMode, StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogDebug("SQLite journal_mode already WAL for {DataSource}", connection.DataSource ?? "memory");
            return;
        }

        var mode = await ExecuteScalarAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        logger?.LogInformation("SQLite journal_mode changed to {JournalMode} for {DataSource}", mode, connection.DataSource ?? "memory");
    }

    private static async Task EnsureSynchronousAsync(SqliteConnection connection, ILogger? logger, CancellationToken cancellationToken)
    {
        var current = await ExecuteScalarAsync(connection, "PRAGMA synchronous;", cancellationToken).ConfigureAwait(false);
        if (string.Equals(current, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(current, RequiredSynchronous, StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogDebug("SQLite synchronous already NORMAL for {DataSource}", connection.DataSource ?? "memory");
            return;
        }

        await ExecuteNonQueryAsync(connection, "PRAGMA synchronous=NORMAL;", cancellationToken).ConfigureAwait(false);
        logger?.LogInformation("SQLite synchronous set to NORMAL for {DataSource}", connection.DataSource ?? "memory");
    }

    private static async Task EnsureBusyTimeoutAsync(SqliteConnection connection, ILogger? logger, CancellationToken cancellationToken)
    {
        var current = await ExecuteScalarAsync(connection, "PRAGMA busy_timeout;", cancellationToken).ConfigureAwait(false);
        if (int.TryParse(current, out var currentTimeout) && currentTimeout == RequiredBusyTimeoutMs)
        {
            logger?.LogDebug("SQLite busy_timeout already {Timeout}ms for {DataSource}", RequiredBusyTimeoutMs, connection.DataSource ?? "memory");
            return;
        }

        await ExecuteNonQueryAsync(connection, $"PRAGMA busy_timeout={RequiredBusyTimeoutMs};", cancellationToken).ConfigureAwait(false);
        logger?.LogInformation("SQLite busy_timeout set to {Timeout}ms for {DataSource}", RequiredBusyTimeoutMs, connection.DataSource ?? "memory");
    }

    public static async Task<SqlitePragmaState> ReadStateAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var journalMode = await ExecuteScalarAsync(connection, "PRAGMA journal_mode;", cancellationToken).ConfigureAwait(false);
        var synchronous = await ExecuteScalarAsync(connection, "PRAGMA synchronous;", cancellationToken).ConfigureAwait(false);
        var busyTimeoutValue = await ExecuteScalarAsync(connection, "PRAGMA busy_timeout;", cancellationToken).ConfigureAwait(false);

        var busyTimeout = int.TryParse(busyTimeoutValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

        return new SqlitePragmaState(journalMode, synchronous, busyTimeout);
    }

    public static bool IsCompliant(SqlitePragmaState state)
    {
        if (!string.Equals(state.NormalizedJournalMode, RequiredJournalMode, StringComparison.Ordinal))
        {
            return false;
        }

        if (state.NormalizedSynchronous is not (RequiredSynchronous or "1"))
        {
            return false;
        }

        return state.BusyTimeout == RequiredBusyTimeoutMs;
    }

    private static async Task<string> ExecuteScalarAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result?.ToString() ?? string.Empty;
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal readonly record struct SqlitePragmaState(string JournalMode, string Synchronous, int BusyTimeout)
    {
        public string NormalizedJournalMode => (JournalMode ?? string.Empty).Trim().ToLowerInvariant();

        public string NormalizedSynchronous => (Synchronous ?? string.Empty).Trim().ToLowerInvariant();
    }
}
