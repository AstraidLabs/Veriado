using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Performs periodic verification of critical SQLite PRAGMA settings and automatically repairs deviations.
/// </summary>
internal sealed class SqlitePragmaHealthCheck : IHealthCheck
{
    private readonly InfrastructureOptions _options;
    private readonly ILogger<SqlitePragmaHealthCheck> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public SqlitePragmaHealthCheck(InfrastructureOptions options, ILogger<SqlitePragmaHealthCheck> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return HealthCheckResult.Unhealthy("SQLite infrastructure has not been initialised with a connection string.");
        }

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
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
