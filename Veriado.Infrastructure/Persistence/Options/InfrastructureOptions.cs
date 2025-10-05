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
    /// Gets or sets the optional directory where the Lucene.NET index should be stored. When
    /// unspecified a path adjacent to the SQLite database file is used.
    /// </summary>
    public string? LuceneIndexPath { get; set; }
        = null;

    /// <summary>
    /// Gets or sets the indexing coordination mode for the Lucene search subsystem.
    /// </summary>
    public SearchIndexingMode SearchIndexingMode { get; set; }
        = SearchIndexingMode.Immediate;

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
/// Enumerates the supported indexing coordination strategies for the Lucene subsystem.
/// </summary>
public enum SearchIndexingMode
{
    /// <summary>
    /// Index mutations are applied immediately within the write worker pipeline.
    /// </summary>
    Immediate,

    /// <summary>
    /// Index mutations are deferred to the outbox processor.
    /// </summary>
    Outbox,
}
