namespace Veriado.Infrastructure.Persistence.Options;

/// <summary>
/// Provides configuration values controlling the behaviour of the infrastructure layer.
/// </summary>
public sealed class InfrastructureOptions
{
    private const int DefaultBatchSize = 300;
    private const int DefaultBatchWindowMs = 250;
    private const int DefaultOutboxBatchSize = 50;
    private const int DefaultRetryBudget = 5;

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
    /// Gets or sets the indexing mode for the FTS5 subsystem.
    /// </summary>
    public FtsIndexingMode FtsIndexingMode { get; set; }
        = FtsIndexingMode.SameTransaction;

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
    /// Gets or sets the maximum number of outbox events fetched in a single drain operation.
    /// </summary>
    public int OutboxBatchSize
    {
        get => _outboxBatchSize;
        set => _outboxBatchSize = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the maximum number of attempts allowed for a single outbox event before moving it to the dead-letter queue.
    /// </summary>
    public int RetryBudget
    {
        get => _retryBudget;
        set => _retryBudget = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private int _batchSize = DefaultBatchSize;
    private int _batchWindowMs = DefaultBatchWindowMs;
    private int _outboxBatchSize = DefaultOutboxBatchSize;
    private int _retryBudget = DefaultRetryBudget;

    internal string? ConnectionString { get; set; }
        = null;
}

/// <summary>
/// Enumerates the supported FTS5 indexing coordination strategies.
/// </summary>
public enum FtsIndexingMode
{
    /// <summary>
    /// FTS changes are executed in the same database transaction as the EF Core persistence layer.
    /// </summary>
    SameTransaction,

    /// <summary>
    /// FTS changes are executed asynchronously using the outbox pattern.
    /// </summary>
    Outbox,
}
