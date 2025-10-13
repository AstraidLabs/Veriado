namespace Veriado.Infrastructure.Persistence.Options;

/// <summary>
/// Provides configuration values controlling the behaviour of the infrastructure layer.
/// </summary>
public sealed class InfrastructureOptions
{
    private const int DefaultBatchSize = 300;
    private const int DefaultBatchWindowMs = 250;

    /// <summary>
    /// Gets or sets the absolute path to the SQLite database file.
    /// </summary>
    public string DbPath { get; set; } = string.Empty;

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
    /// Gets or sets the maximum number of work items processed in a single batch.
    /// </summary>
    public int BatchSize
    {
        get => _batchSize;
        set => _batchSize = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the window length in milliseconds used for batching write operations.
    /// </summary>
    public int BatchWindowMs
    {
        get => _batchWindowMs;
        set => _batchWindowMs = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
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

    private int _batchSize = DefaultBatchSize;
    private int _batchWindowMs = DefaultBatchWindowMs;

    internal string? ConnectionString { get; set; }
        = null;
}
