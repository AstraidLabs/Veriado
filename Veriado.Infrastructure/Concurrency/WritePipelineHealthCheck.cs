using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Veriado.Infrastructure.Concurrency;

/// <summary>
/// Reports health information for the queued write pipeline.
/// </summary>
internal sealed class WritePipelineHealthCheck : IHealthCheck
{
    private readonly WritePipelineState _state;
    private readonly InfrastructureOptions _options;

    public WritePipelineHealthCheck(WritePipelineState state, InfrastructureOptions options)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var queueDepth = _state.TotalQueueDepth;
        var averageLatencyMs = _state.AverageQueueLatencyMs;
        var lastCompletedUtc = _state.LastBatchCompletedUtc;
        var lastDuration = _state.LastBatchDuration;
        var lastRetries = _state.LastBatchRetryCount;
        var lastSize = _state.LastBatchSize;
        var lastPartition = _state.LastBatchPartitionId;
        var stall = DateTimeOffset.UtcNow - lastCompletedUtc;
        var stallMs = Math.Max(0d, stall.TotalMilliseconds);

        var status = HealthStatus.Healthy;
        var issues = new List<string>();

        if (queueDepth > _options.HealthQueueDepthWarn)
        {
            status = HealthStatus.Degraded;
            issues.Add($"Queue depth {queueDepth} exceeds warning threshold {_options.HealthQueueDepthWarn}.");
        }

        if (stallMs > _options.HealthWorkerStallMs)
        {
            status = HealthStatus.Degraded;
            issues.Add($"Last batch completed {stallMs:F0} ms ago (threshold {_options.HealthWorkerStallMs} ms).");
        }

        var description = issues.Count == 0
            ? "Write pipeline operating normally."
            : string.Join(' ', issues);

        var data = new Dictionary<string, object?>
        {
            ["queueDepth"] = queueDepth,
            ["averageQueueLatencyMs"] = averageLatencyMs,
            ["lastBatchCompletedUtc"] = lastCompletedUtc,
            ["lastBatchDurationMs"] = lastDuration.TotalMilliseconds,
            ["lastBatchRetries"] = lastRetries,
            ["lastBatchSize"] = lastSize,
            ["lastBatchPartition"] = lastPartition,
        };

        return Task.FromResult(new HealthCheckResult(status, description, null, data));
    }
}
