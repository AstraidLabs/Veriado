using Veriado.Appl.Pipeline.Idempotency;
using Veriado.Infrastructure.Persistence.Connections;

namespace Veriado.Infrastructure.Idempotency;

/// <summary>
/// Provides a SQLite-backed implementation of the idempotency store.
/// </summary>
internal sealed class SqliteIdempotencyStore : IIdempotencyStore
{
    private readonly IClock _clock;
    private readonly IConnectionStringProvider _connectionStringProvider;

    public SqliteIdempotencyStore(
        IConnectionStringProvider connectionStringProvider,
        IClock clock)
    {
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        _clock = clock;
    }

    public async Task<bool> TryRegisterAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);
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
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);
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
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM idempotency_keys WHERE key = $key;";
        command.Parameters.Add("$key", SqliteType.Text).Value = FormatKey(requestId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private SqliteConnection CreateConnection()
        => _connectionStringProvider.CreateConnection();

    private static string FormatKey(Guid requestId) => requestId.ToString("N", CultureInfo.InvariantCulture);
}
