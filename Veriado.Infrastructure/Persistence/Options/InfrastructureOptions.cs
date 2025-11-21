using System.ComponentModel.DataAnnotations;

namespace Veriado.Infrastructure.Persistence.Options;

/// <summary>
/// Provides configuration values controlling the behaviour of the infrastructure layer.
/// </summary>
public sealed class InfrastructureOptions
{
    private const int DefaultWriteQueueCapacity = 10_000;
    private const int DefaultBatchMaxItems = 300;
    private const int DefaultBatchWindowMs = 250;
    private const int DefaultWorkerCount = 1;
    private const int DefaultHealthQueueDepthWarn = 8_000;
    private const int DefaultHealthWorkerStallMs = 30_000;
    private const int DefaultIntegrityBatchSize = 2000;
    private const int DefaultIntegrityTimeSliceMs = 0;

    /// <summary>
    /// Gets or sets the absolute path to the SQLite database file.
    /// </summary>
    public string DbPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional override for the file storage root path.
    /// </summary>
    public string? StorageRootOverride { get; set; }
        = null;

    /// <summary>
    /// Gets or sets the optional maximum number of bytes allowed for stored file content.
    /// </summary>
    public int? MaxContentBytes { get; set; }
        = null;

    /// <summary>
    /// Gets a value indicating whether the runtime environment provides the required SQLite FTS5 features.
    /// </summary>
    public bool IsFulltextAvailable { get; internal set; } = true;

    /// <summary>
    /// Gets the last detected failure reason when SQLite FTS5 support is unavailable.
    /// </summary>
    public string? FulltextAvailabilityError { get; internal set; }
        = null;

    /// <summary>
    /// Gets or sets the maximum number of pending write requests the queue can hold across all partitions.
    /// </summary>
    public int WriteQueueCapacity
    {
        get => _writeQueueCapacity;
        set => _writeQueueCapacity = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the maximum number of work items processed in a single batch.
    /// </summary>
    public int BatchMaxItems
    {
        get => _batchMaxItems;
        set => _batchMaxItems = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the window length in milliseconds used for batching write operations.
    /// </summary>
    public int BatchMaxWindowMs
    {
        get => _batchWindowMs;
        set => _batchWindowMs = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the number of worker partitions processing the write queue.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int Workers
    {
        get => _workers;
        set => _workers = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the queue depth threshold that triggers a degraded write health status.
    /// </summary>
    public int HealthQueueDepthWarn
    {
        get => _healthQueueDepthWarn;
        set => _healthQueueDepthWarn = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the stall threshold (in milliseconds) that triggers a degraded write health status.
    /// </summary>
    public int HealthWorkerStallMs
    {
        get => _healthWorkerStallMs;
        set => _healthWorkerStallMs = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the number of rows processed per batch when verifying full-text integrity.
    /// </summary>
    public int IntegrityBatchSize
    {
        get => _integrityBatchSize;
        set => _integrityBatchSize = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the maximum number of milliseconds spent verifying integrity before pausing.
    /// </summary>
    public int IntegrityTimeSliceMs
    {
        get => _integrityTimeSliceMs;
        set => _integrityTimeSliceMs = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Gets or sets a value indicating whether full-text integrity verification should run automatically at startup.
    /// </summary>
    public bool RunIntegrityCheckOnStartup { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether integrity verification should attempt automatic repairs when inconsistencies are detected.
    /// </summary>
    public bool RepairIntegrityAutomatically { get; set; } = true;

    /// <summary>
    /// Gets or sets the duration after which stored idempotency keys expire.
    /// </summary>
    public TimeSpan IdempotencyKeyTtl { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Gets or sets the interval at which expired idempotency keys are purged.
    /// </summary>
    public TimeSpan IdempotencyCleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the interval at which the background index auditor verifies FTS consistency.
    /// </summary>
    public TimeSpan IndexAuditInterval { get; set; } = TimeSpan.FromHours(4);

    private int _writeQueueCapacity = DefaultWriteQueueCapacity;
    private int _batchMaxItems = DefaultBatchMaxItems;
    private int _batchWindowMs = DefaultBatchWindowMs;
    private int _workers = DefaultWorkerCount;
    private int _healthQueueDepthWarn = DefaultHealthQueueDepthWarn;
    private int _healthWorkerStallMs = DefaultHealthWorkerStallMs;
    private int _integrityBatchSize = DefaultIntegrityBatchSize;
    private int _integrityTimeSliceMs = DefaultIntegrityTimeSliceMs;
}
