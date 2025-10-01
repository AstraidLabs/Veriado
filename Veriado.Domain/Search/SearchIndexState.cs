namespace Veriado.Domain.Search;

/// <summary>
/// Represents the indexing state for a file within the full-text search subsystem.
/// </summary>
public sealed class SearchIndexState
{
    /// <summary>
    /// Gets the default analyzer version recorded for indexed documents.
    /// </summary>
    public const string DefaultAnalyzerVersion = "v1";

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchIndexState"/> class.
    /// </summary>
    /// <param name="schemaVersion">The schema version used by the index.</param>
    /// <param name="isStale">Indicates whether the index entry requires reindexing.</param>
    /// <param name="lastIndexedUtc">The timestamp of the last successful indexing operation.</param>
    /// <param name="indexedContentHash">The content hash stored in the index.</param>
    /// <param name="indexedTitle">The title stored in the index.</param>
    public SearchIndexState(
        int schemaVersion,
        bool isStale = false,
        DateTimeOffset? lastIndexedUtc = null,
        string? indexedContentHash = null,
        string? indexedTitle = null,
        string? analyzerVersion = null,
        string? tokenHash = null)
    {
        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "Schema version must be positive.");
        }

        SchemaVersion = schemaVersion;
        IsStale = isStale;
        LastIndexedUtc = lastIndexedUtc?.ToUniversalTime();
        IndexedContentHash = indexedContentHash;
        IndexedTitle = indexedTitle;
        AnalyzerVersion = string.IsNullOrWhiteSpace(analyzerVersion)
            ? DefaultAnalyzerVersion
            : analyzerVersion;
        TokenHash = tokenHash;
    }

    /// <summary>
    /// Gets a value indicating whether the indexed document is stale.
    /// </summary>
    public bool IsStale { get; private set; }

    /// <summary>
    /// Gets the schema version against which the document has been indexed.
    /// </summary>
    public int SchemaVersion { get; private set; }

    /// <summary>
    /// Gets the timestamp of the last indexing operation, if any.
    /// </summary>
    public DateTimeOffset? LastIndexedUtc { get; private set; }

    /// <summary>
    /// Gets the hash of the content stored in the index, if known.
    /// </summary>
    public string? IndexedContentHash { get; private set; }

    /// <summary>
    /// Gets the title stored in the index, if known.
    /// </summary>
    public string? IndexedTitle { get; private set; }

    /// <summary>
    /// Gets the analyzer version used when producing the indexed tokens.
    /// </summary>
    public string AnalyzerVersion { get; private set; } = DefaultAnalyzerVersion;

    /// <summary>
    /// Gets the hash of the analyzer token output stored for drift detection.
    /// </summary>
    public string? TokenHash { get; private set; }

    /// <summary>
    /// Marks the document as stale, returning whether the state changed.
    /// </summary>
    /// <returns><see langword="true"/> if the state transitioned to stale; otherwise <see langword="false"/>.</returns>
    public bool MarkStale()
    {
        if (IsStale)
        {
            return false;
        }

        IsStale = true;
        return true;
    }

    /// <summary>
    /// Applies the result of a successful indexing operation.
    /// </summary>
    /// <param name="schemaVersion">The schema version applied by the indexer.</param>
    /// <param name="indexedUtc">The timestamp of indexing completion.</param>
    /// <param name="contentHash">The content hash stored in the index.</param>
    /// <param name="title">The title stored in the index.</param>
    public void ApplyIndexed(
        int schemaVersion,
        DateTimeOffset indexedUtc,
        string? contentHash,
        string? title,
        string analyzerVersion,
        string? tokenHash)
    {
        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "Schema version must be positive.");
        }

        if (string.IsNullOrWhiteSpace(analyzerVersion))
        {
            throw new ArgumentException("Analyzer version cannot be null or whitespace.", nameof(analyzerVersion));
        }

        SchemaVersion = schemaVersion;
        LastIndexedUtc = indexedUtc.ToUniversalTime();
        IndexedContentHash = contentHash;
        IndexedTitle = title;
        AnalyzerVersion = analyzerVersion;
        TokenHash = tokenHash;
        IsStale = false;
    }
}
