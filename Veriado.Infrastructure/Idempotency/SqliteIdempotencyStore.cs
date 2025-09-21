using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Veriado.Application.Abstractions;
using Veriado.Application.Pipeline.Idempotency;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Idempotency;

/// <summary>
/// Provides a SQLite-backed implementation of the idempotency store.
/// </summary>
internal sealed class SqliteIdempotencyStore : IIdempotencyStore
{
    private readonly InfrastructureOptions _options;
    private readonly IClock _clock;

    public SqliteIdempotencyStore(InfrastructureOptions options, IClock clock)
    {
        _options = options;
        _clock = clock;
    }

    public async Task<bool> TryRegisterAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR IGNORE INTO idempotency_keys(key, created_utc) VALUES ($key, $createdUtc);";
        command.Parameters.Add("$key", SqliteType.Text).Value = FormatKey(requestId);
        command.Parameters.Add("$createdUtc", SqliteType.Text).Value = _clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    public async Task MarkProcessedAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE idempotency_keys SET created_utc = $createdUtc WHERE key = $key;";
        command.Parameters.Add("$key", SqliteType.Text).Value = FormatKey(requestId);
        command.Parameters.Add("$createdUtc", SqliteType.Text).Value = _clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM idempotency_keys WHERE key = $key;";
        command.Parameters.Add("$key", SqliteType.Text).Value = FormatKey(requestId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private SqliteConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
        }

        return new SqliteConnection(_options.ConnectionString);
    }

    private static string FormatKey(Guid requestId) => requestId.ToString("N", CultureInfo.InvariantCulture);
}
