using System;
using Veriado.Infrastructure.Search;

namespace Veriado.Infrastructure.Maintenance;

/// <summary>
/// Provides SQLite specific database maintenance operations.
/// </summary>
internal sealed class SqliteDatabaseMaintenanceService : IDatabaseMaintenanceService
{
    private readonly InfrastructureOptions _options;
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteDatabaseMaintenanceService(InfrastructureOptions options, ISqliteConnectionFactory connectionFactory)
    {
        _options = options;
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<int> VacuumAndOptimizeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
        }

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
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
        }

        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
