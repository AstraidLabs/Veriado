using System;

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
    /// Gets or sets a value indicating whether the key/value metadata store should be used instead of JSON.
    /// </summary>
    public bool UseKvMetadata { get; set; }
        = false;

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
    public bool RepairIntegrityAutomatically { get; set; } = false;

    /// <summary>
    /// Gets or sets the duration after which stored idempotency keys expire.
    /// </summary>
    public TimeSpan IdempotencyKeyTtl { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Gets or sets the interval at which expired idempotency keys are purged.
    /// </summary>
    public TimeSpan IdempotencyCleanupInterval { get; set; } = TimeSpan.FromHours(1);

    private int _batchSize = DefaultBatchSize;
    private int _batchWindowMs = DefaultBatchWindowMs;

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
