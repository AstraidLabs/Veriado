using System;

namespace Veriado.Infrastructure.Concurrency;

/// <summary>
/// Represents configurable settings for the search write pipeline.
/// </summary>
public sealed class SearchWriteOptions
{
    public int MaxBatchSize { get; set; } = 128;

    public int MaxBatchDurationMs { get; set; } = 5_000;

    public int MaxQueueDepthWarning { get; set; } = 5_000;

    public bool EnableMultiWorker { get; set; }
        = false;

    public int Workers { get; set; } = 1;

    public string ShardKey { get; set; } = "DocumentId";

    public TimeSpan FtsRedetectInterval { get; set; } = TimeSpan.FromMinutes(5);

    public int SqliteBusyTimeoutMs { get; set; } = 250;

    public int RetryMaxAttempts { get; set; } = 3;

    public int RetryBaseDelayMs { get; set; } = 100;

    public int BatchWindowMs { get; set; } = 250;
}
