namespace Veriado.Appl.Abstractions;

/// <summary>
/// Provides access to diagnostics and health information exposed by the infrastructure layer.
/// </summary>
public interface IDiagnosticsRepository
{
    /// <summary>
    /// Retrieves general health information about the database.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<DatabaseHealthSnapshot> GetDatabaseHealthAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves statistics about the full-text search index.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<SearchIndexSnapshot> GetIndexStatisticsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Represents diagnostic information about the underlying SQLite database.
/// </summary>
/// <param name="DatabasePath">The absolute database file path.</param>
/// <param name="JournalMode">The configured journal mode.</param>
/// <param name="IsWalEnabled">Indicates whether WAL journaling is enabled.</param>
/// <param name="PendingOutboxEvents">Number of pending outbox events awaiting processing.</param>
public sealed record DatabaseHealthSnapshot(
    string DatabasePath,
    string JournalMode,
    bool IsWalEnabled,
    int PendingOutboxEvents);

/// <summary>
/// Represents aggregate statistics about the search index.
/// </summary>
/// <param name="TotalDocuments">Total number of indexed documents.</param>
/// <param name="StaleDocuments">Number of documents marked as stale.</param>
/// <param name="LuceneVersion">The Lucene.NET version string.</param>
public sealed record SearchIndexSnapshot(
    int TotalDocuments,
    int StaleDocuments,
    string? LuceneVersion);
