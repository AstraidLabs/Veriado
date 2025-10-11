using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Reports the health of the FTS write-ahead dead-letter queue.
/// </summary>
internal sealed class FtsDlqHealthCheck : IHealthCheck
{
    private const int HealthyThreshold = 100;
    private const int DegradedThreshold = 1000;

    private readonly IFtsDlqMonitor _monitor;
    private readonly ILogger<FtsDlqHealthCheck> _logger;

    public FtsDlqHealthCheck(IFtsDlqMonitor monitor, ILogger<FtsDlqHealthCheck> logger)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await _monitor.GetDlqCountAsync(cancellationToken).ConfigureAwait(false);
            var data = new Dictionary<string, object?>
            {
                ["dlqCount"] = count,
            };

            if (count < HealthyThreshold)
            {
                return HealthCheckResult.Healthy(
                    $"FTS write-ahead DLQ contains {count} entries.",
                    data);
            }

            if (count < DegradedThreshold)
            {
                return HealthCheckResult.Degraded(
                    $"FTS write-ahead DLQ backlog is {count} entries.",
                    data: data);
            }

            return HealthCheckResult.Unhealthy(
                $"FTS write-ahead DLQ backlog is {count} entries.",
                data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate FTS write-ahead DLQ health.");
            return HealthCheckResult.Unhealthy(
                "Failed to query FTS write-ahead DLQ backlog.",
                ex);
        }
    }
}
