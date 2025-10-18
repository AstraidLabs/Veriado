using Microsoft.Extensions.Diagnostics.HealthChecks;
using Veriado.Infrastructure.Persistence.Connections;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Performs periodic verification of critical SQLite PRAGMA settings and automatically repairs deviations.
/// </summary>
internal sealed class SqlitePragmaHealthCheck : IHealthCheck
{
    private readonly ILogger<SqlitePragmaHealthCheck> _logger;
    private readonly IConnectionStringProvider _connectionStringProvider;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public SqlitePragmaHealthCheck(
        ILogger<SqlitePragmaHealthCheck> logger,
        IConnectionStringProvider connectionStringProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = _connectionStringProvider.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var current = await SqlitePragmaHelper.ReadStateAsync(connection, cancellationToken).ConfigureAwait(false);
            if (SqlitePragmaHelper.IsCompliant(current))
            {
                return HealthCheckResult.Healthy(
                    "SQLite PRAGMA settings are configured as expected.",
                    BuildData(current));
            }

            _logger.LogWarning(
                "SQLite PRAGMA settings deviated from expected values (journal_mode={JournalMode}, synchronous={Synchronous}, busy_timeout={BusyTimeout}). Applying corrective action.",
                current.JournalMode,
                current.Synchronous,
                current.BusyTimeout);

            await SqlitePragmaHelper.ApplyAsync(connection, _logger, cancellationToken).ConfigureAwait(false);
            var repaired = await SqlitePragmaHelper.ReadStateAsync(connection, cancellationToken).ConfigureAwait(false);

            if (!SqlitePragmaHelper.IsCompliant(repaired))
            {
                return HealthCheckResult.Unhealthy(
                    "SQLite PRAGMA settings remain non-compliant after automatic remediation attempts.",
                    data: BuildData(repaired));
            }

            return HealthCheckResult.Degraded(
                "SQLite PRAGMA settings required corrective action. Values were updated automatically.",
                data: BuildData(repaired));
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static IReadOnlyDictionary<string, object?> BuildData(SqlitePragmaHelper.SqlitePragmaState state)
        => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["journal_mode"] = state.JournalMode,
            ["synchronous"] = state.Synchronous,
            ["busy_timeout"] = state.BusyTimeout,
        };
}
