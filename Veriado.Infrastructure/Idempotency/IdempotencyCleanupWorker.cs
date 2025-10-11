using Microsoft.Extensions.Hosting;

namespace Veriado.Infrastructure.Idempotency;

/// <summary>
/// Periodically removes expired idempotency keys from the backing store.
/// </summary>
internal sealed class IdempotencyCleanupWorker : BackgroundService
{
    private readonly InfrastructureOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<IdempotencyCleanupWorker> _logger;

    public IdempotencyCleanupWorker(
        InfrastructureOptions options,
        IClock clock,
        ILogger<IdempotencyCleanupWorker> logger)
    {
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.IdempotencyKeyTtl <= TimeSpan.Zero)
        {
            _logger.LogInformation("Idempotency cleanup worker disabled (TTL not configured)");
            return;
        }

        var delay = _options.IdempotencyCleanupInterval <= TimeSpan.Zero
            ? TimeSpan.FromHours(1)
            : _options.IdempotencyCleanupInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean expired idempotency keys");
            }

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            _logger.LogDebug("Skipping idempotency cleanup because connection string is not available yet");
            return;
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);

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
