using System;
using Veriado.Infrastructure.Search;

namespace Veriado.Infrastructure.Maintenance;

/// <summary>
/// Provides SQLite specific database maintenance operations.
/// </summary>
internal sealed class SqliteDatabaseMaintenanceService : IDatabaseMaintenanceService
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteDatabaseMaintenanceService(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
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
}
