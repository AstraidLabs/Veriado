using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Search;

namespace Veriado.Infrastructure.Idempotency;

/// <summary>
/// Periodically removes expired idempotency keys from the backing store.
/// </summary>
internal sealed class IdempotencyCleanupWorker : BackgroundService
{
    private readonly InfrastructureOptions _options;
    private readonly IClock _clock;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<IdempotencyCleanupWorker> _logger;

    public IdempotencyCleanupWorker(
        InfrastructureOptions options,
        IClock clock,
        ISqliteConnectionFactory connectionFactory,
        ILogger<IdempotencyCleanupWorker> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.IdempotencyKeyTtl <= TimeSpan.Zero)
        {
            _logger.LogInformation("Idempotency cleanup worker disabled (TTL not configured)");
            return;
        }

        var interval = _options.IdempotencyCleanupInterval <= TimeSpan.Zero
            ? TimeSpan.FromHours(1)
            : _options.IdempotencyCleanupInterval;

        _logger.LogInformation(
            "{WorkerName} started with interval {Interval}.",
            nameof(IdempotencyCleanupWorker),
            interval);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ExecuteCleanupAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("{WorkerName} is stopping (canceled).", nameof(IdempotencyCleanupWorker));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {WorkerName}.", nameof(IdempotencyCleanupWorker));
        }
        finally
        {
            _logger.LogInformation("{WorkerName} stopped.", nameof(IdempotencyCleanupWorker));
        }
    }

    private async Task ExecuteCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CleanupAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean expired idempotency keys");
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'idempotency_keys' LIMIT 1;";
        var tableExists = await existsCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (tableExists is null || tableExists == DBNull.Value)
        {
            _logger.LogDebug("Skipping idempotency cleanup because the idempotency_keys table does not exist yet");
            return;
        }

        var cutoff = _clock.UtcNow - _options.IdempotencyKeyTtl;
        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.CommandText = "DELETE FROM idempotency_keys WHERE created_utc < $cutoff;";
        deleteCommand.Parameters.Add("$cutoff", SqliteType.Text).Value =
            cutoff.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        int removed;
        try
        {
            removed = await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(ex, "Skipping idempotency cleanup because the idempotency_keys table does not exist yet");
            return;
        }

        if (removed > 0)
        {
            _logger.LogInformation("Removed {Removed} expired idempotency keys", removed);
        }
    }
}
