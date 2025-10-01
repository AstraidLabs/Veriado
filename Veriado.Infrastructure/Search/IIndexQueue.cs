namespace Veriado.Infrastructure.Search;

/// <summary>
/// Represents a lightweight queue used to schedule search index repair operations.
/// </summary>
public interface IIndexQueue
{
    /// <summary>
    /// Enqueues a document for reindexing.
    /// </summary>
    /// <param name="document">The document to reindex.</param>
    void Enqueue(IndexDocument document);
}

/// <summary>
/// Represents a queued search index repair document.
/// </summary>
/// <param name="FileId">The identifier of the file to reindex.</param>
public sealed record IndexDocument(string FileId);
