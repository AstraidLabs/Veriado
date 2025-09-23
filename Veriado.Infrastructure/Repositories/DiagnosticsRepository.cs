using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Veriado.Appl.Abstractions;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Repositories;

/// <summary>
/// Provides diagnostics and statistical information about the SQLite database.
/// </summary>
internal sealed class DiagnosticsRepository : IDiagnosticsRepository
{
    private readonly InfrastructureOptions _options;

    public DiagnosticsRepository(InfrastructureOptions options)
    {
        _options = options;
    }

    public async Task<DatabaseHealthSnapshot> GetDatabaseHealthAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var journalMode = await GetScalarAsync(connection, "PRAGMA journal_mode;", cancellationToken).ConfigureAwait(false);
        var isWal = string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase);

        var pendingOutbox = 0;
        if (_options.FtsIndexingMode == FtsIndexingMode.Outbox)
        {
            var pending = await GetScalarAsync(connection, "SELECT COUNT(*) FROM outbox_events WHERE processed_utc IS NULL;", cancellationToken)
                .ConfigureAwait(false);
            if (long.TryParse(pending, out var count))
            {
                pendingOutbox = (int)count;
            }
        }

        return new DatabaseHealthSnapshot(_options.DbPath, journalMode, isWal, pendingOutbox);
    }

    public async Task<SearchIndexSnapshot> GetIndexStatisticsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var total = await GetScalarAsync(connection, "SELECT COUNT(*) FROM files;", cancellationToken).ConfigureAwait(false);
        var stale = await GetScalarAsync(connection, "SELECT COUNT(*) FROM files WHERE fts_is_stale = 1;", cancellationToken).ConfigureAwait(false);
        var version = await GetScalarAsync(connection, "SELECT fts5();", cancellationToken).ConfigureAwait(false);

        var totalCount = int.TryParse(total, out var parsedTotal) ? parsedTotal : 0;
        var staleCount = int.TryParse(stale, out var parsedStale) ? parsedStale : 0;

        return new SearchIndexSnapshot(totalCount, staleCount, string.IsNullOrWhiteSpace(version) ? null : version);
    }

    private static async Task<string> GetScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result?.ToString() ?? string.Empty;
    }
}
