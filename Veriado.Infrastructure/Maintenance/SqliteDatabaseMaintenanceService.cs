using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Veriado.Appl.Abstractions;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Maintenance;

/// <summary>
/// Provides SQLite specific database maintenance operations.
/// </summary>
internal sealed class SqliteDatabaseMaintenanceService : IDatabaseMaintenanceService
{
    private readonly InfrastructureOptions _options;

    public SqliteDatabaseMaintenanceService(InfrastructureOptions options)
    {
        _options = options;
    }

    public async Task<int> VacuumAndOptimizeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
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
}
