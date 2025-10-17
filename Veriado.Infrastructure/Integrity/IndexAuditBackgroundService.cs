using Microsoft.Extensions.Hosting;

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Integrity;

/// <summary>
/// Periodically executes the <see cref="IIndexAuditor"/> to detect and repair FTS drift.
/// </summary>
internal sealed class IndexAuditBackgroundService : BackgroundService
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(1);

    private readonly IIndexAuditor _auditor;
    private readonly InfrastructureOptions _options;
    private readonly ILogger<IndexAuditBackgroundService> _logger;

    public IndexAuditBackgroundService(
        IIndexAuditor auditor,
        InfrastructureOptions options,
        ILogger<IndexAuditBackgroundService> logger)
    {
        _auditor = auditor ?? throw new ArgumentNullException(nameof(auditor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = _options.IndexAuditInterval;
        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromHours(4);
        }

        if (interval < MinimumInterval)
        {
            interval = MinimumInterval;
        }

        _logger.LogInformation("Index audit background service started with interval {Interval}.", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAuditAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Index audit execution failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Index audit background service stopping.");
    }

    private async Task RunAuditAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Running periodic FTS audit.");
        var summary = await _auditor.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var issues = (summary.Missing?.Count ?? 0) + (summary.Drift?.Count ?? 0) + (summary.Extra?.Count ?? 0);

        if (issues == 0)
        {
            _logger.LogDebug("Periodic FTS audit completed without discrepancies.");
            return;
        }

        var scheduled = await _auditor.RepairDriftAsync(summary, cancellationToken).ConfigureAwait(false);
        if (scheduled > 0)
        {
            _logger.LogInformation(
                "Periodic FTS audit detected {IssueCount} discrepancies and scheduled {ScheduledCount} reindex operations.",
                issues,
                scheduled);
        }
        else
        {
            _logger.LogInformation(
                "Periodic FTS audit detected {IssueCount} discrepancies; automated reindexing is disabled in this phase.",
                issues);
        }
    }
}
