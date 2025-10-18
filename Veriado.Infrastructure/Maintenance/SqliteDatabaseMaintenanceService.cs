using System;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Search;

namespace Veriado.Infrastructure.Maintenance;

/// <summary>
/// Provides SQLite specific database maintenance operations.
/// </summary>
internal sealed class SqliteDatabaseMaintenanceService : IDatabaseMaintenanceService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteDatabaseMaintenanceService> _logger;

    public SqliteDatabaseMaintenanceService(
        ISqliteConnectionFactory connectionFactory,
        ILogger<SqliteDatabaseMaintenanceService> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> VacuumAndOptimizeAsync(CancellationToken cancellationToken)
    {
        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var statements = new[] { "VACUUM;", "PRAGMA optimize;" };
        var executed = 0;

        foreach (var sql in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            executed++;
        }

        return executed;
    }

    public async Task RehydrateWalAsync(CancellationToken cancellationToken)
    {
        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RebuildFulltextIndexAsync(CancellationToken cancellationToken)
    {
        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = (SqliteConnection)lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Ensuring unified FTS schema prior to rebuild.");
        await SqliteFulltextSchemaManager
            .EnsureUnifiedSchemaAsync(connection, _logger, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Triggering full-text index rebuild.");
        return await SqliteFulltextSchemaManager
            .ReindexAsync(connection, _logger, cancellationToken)
            .ConfigureAwait(false);
    }
}
