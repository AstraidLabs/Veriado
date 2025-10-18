using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Persistence.Connections;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Verifies that the SQLite full-text schema matches the unified contentless configuration.
/// </summary>
internal sealed class SqliteFulltextSchemaHealthCheck : IHealthCheck
{
    private readonly IConnectionStringProvider _connectionStringProvider;
    private readonly ILogger<SqliteFulltextSchemaHealthCheck> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public SqliteFulltextSchemaHealthCheck(
        IConnectionStringProvider connectionStringProvider,
        ILogger<SqliteFulltextSchemaHealthCheck> logger)
    {
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = _connectionStringProvider.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var inspection = await SqliteFulltextSchemaInspector
                .InspectAsync((SqliteConnection)connection, cancellationToken)
                .ConfigureAwait(false);
            SqliteFulltextSupport.UpdateSchemaSnapshot(inspection.Snapshot);

            if (inspection.IsValid)
            {
                return HealthCheckResult.Healthy(
                    "SQLite full-text schema matches the unified contentless definition.",
                    BuildData(inspection));
            }

            var reason = inspection.FailureReason ?? "Unknown FTS schema mismatch.";
            _logger.LogWarning("FTS schema health check detected inconsistency: {Reason}", reason);

            return HealthCheckResult.Unhealthy(
                reason,
                data: BuildData(inspection));
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static IReadOnlyDictionary<string, object?> BuildData(FulltextSchemaInspectionResult inspection)
        => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["missingFtsColumns"] = string.Join(", ", inspection.MissingFtsColumns),
            ["missingDocumentColumns"] = string.Join(", ", inspection.MissingDocumentColumns),
            ["missingTriggers"] = string.Join(", ", inspection.MissingTriggers),
            ["isContentless"] = inspection.Snapshot.IsContentless,
        };
}
