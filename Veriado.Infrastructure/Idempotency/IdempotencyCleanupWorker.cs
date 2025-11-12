using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Search;
using Veriado.Infrastructure.Lifecycle;

namespace Veriado.Infrastructure.Idempotency;

/// <summary>
/// Periodically removes expired idempotency keys from the backing store.
/// </summary>
internal sealed class IdempotencyCleanupWorker : BackgroundService
{
    private static readonly TimeSpan IterationTimeout = TimeSpan.FromMinutes(5);

    private readonly InfrastructureOptions _options;
    private readonly IClock _clock;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<IdempotencyCleanupWorker> _logger;
    private readonly IAppLifecycleService _lifecycleService;

    public IdempotencyCleanupWorker(
        InfrastructureOptions options,
        IClock clock,
        ISqliteConnectionFactory connectionFactory,
        IAppLifecycleService lifecycleService,
        ILogger<IdempotencyCleanupWorker> logger)
    {
        _options = options;
        _clock = clock;
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _lifecycleService = lifecycleService ?? throw new ArgumentNullException(nameof(lifecycleService));
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

        while (!stoppingToken.IsCancellationRequested && !_lifecycleService.RunToken.IsCancellationRequested)
        {
            using var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _lifecycleService.RunToken);
            iterationCts.CancelAfter(IterationTimeout);
            var iterationToken = iterationCts.Token;

            try
            {
                await _lifecycleService.PauseToken.WaitIfPausedAsync(iterationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (iterationToken.IsCancellationRequested)
            {
                if (stoppingToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            try
            {
                await CleanupAsync(iterationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (iterationToken.IsCancellationRequested)
            {
                if (stoppingToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
                {
                    break;
                }

                if (iterationCts.IsCancellationRequested)
                {
                    _logger.LogWarning("Idempotency cleanup iteration timed out after {Timeout}.", IterationTimeout);
                }

                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean expired idempotency keys");
            }

            try
            {
                await Task.Delay(delay, iterationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (iterationToken.IsCancellationRequested)
            {
                if (stoppingToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
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
