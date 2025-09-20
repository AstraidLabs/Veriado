using System;

namespace Veriado.Domain.Search;

/// <summary>
/// Tracks synchronization between the file aggregate and the search index.
/// </summary>
public sealed class SearchIndexState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchIndexState"/> class.
    /// </summary>
    /// <param name="schemaVersion">Current schema version known to the index.</param>
    /// <param name="isStale">Optional flag to initialize as stale.</param>
    public SearchIndexState(int schemaVersion = 0, bool isStale = true)
    {
        if (schemaVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "Schema version cannot be negative.");
        }

        SchemaVersion = schemaVersion;
        IsStale = isStale;
    }

    /// <summary>
    /// Gets a value indicating whether the indexed representation is stale.
    /// </summary>
    public bool IsStale { get; private set; }

    /// <summary>
    /// Gets the schema version for which the file was last indexed.
    /// </summary>
    public int SchemaVersion { get; private set; }

    /// <summary>
    /// Gets the timestamp when the file was last indexed, if known.
    /// </summary>
    public DateTimeOffset? LastIndexedUtc { get; private set; }

    /// <summary>
    /// Gets the hash of the content that was last indexed.
    /// </summary>
    public string? IndexedContentHash { get; private set; }

    /// <summary>
    /// Gets the title that was last indexed.
    /// </summary>
    public string? IndexedTitle { get; private set; }

    /// <summary>
    /// Marks the indexed representation as stale.
    /// </summary>
    public void MarkStale() => IsStale = true;

    /// <summary>
    /// Confirms the index is up-to-date with the provided schema version.
    /// </summary>
    /// <param name="schemaVersion">Schema version used during indexing.</param>
    /// <param name="contentHash">Hash of the indexed content.</param>
    /// <param name="indexedTitle">Title used when indexing.</param>
    /// <param name="whenUtc">Timestamp of indexing.</param>
    public void ConfirmIndexed(int schemaVersion, string contentHash, string indexedTitle, DateTimeOffset whenUtc)
    {
        if (schemaVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "Schema version cannot be negative.");
        }

        SchemaVersion = schemaVersion;
        IndexedContentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
        IndexedTitle = indexedTitle ?? throw new ArgumentNullException(nameof(indexedTitle));
        LastIndexedUtc = EnsureUtc(whenUtc);
        IsStale = false;
    }

    /// <summary>
    /// Updates the schema version and marks the index as stale when the version increases.
    /// </summary>
    /// <param name="newSchemaVersion">New schema version.</param>
    /// <returns><c>true</c> when the schema version was increased and the index is now stale.</returns>
    public bool BumpSchemaVersion(int newSchemaVersion)
    {
        if (newSchemaVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newSchemaVersion), newSchemaVersion, "Schema version cannot be negative.");
        }

        if (newSchemaVersion > SchemaVersion)
        {
            SchemaVersion = newSchemaVersion;
            IsStale = true;
            return true;
        }

        return false;
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset timestamp) => timestamp.ToUniversalTime();
}
