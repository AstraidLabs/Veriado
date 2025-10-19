namespace Veriado.Appl.Abstractions;

/// <summary>
/// Provides synchronous projection helpers for file aggregates into the search index.
/// </summary>
public interface IFileSearchProjection
{
    /// <summary>
    /// Upserts the specified file aggregate into the search projection store using optimistic concurrency guards.
    /// </summary>
    /// <param name="file">The aggregate to project.</param>
    /// <param name="expectedContentHash">The content hash currently recorded in the index.</param>
    /// <param name="newContentHash">The new content hash to persist.</param>
    /// <param name="tokenHash">The analyzer token hash captured for the projection.</param>
    /// <param name="guard">The projection transaction guard.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UpsertAsync(
        FileEntity file,
        string? expectedContentHash,
        string? expectedTokenHash,
        string? newContentHash,
        string? tokenHash,
        CancellationToken cancellationToken);

    /// <summary>
    /// Forces the projection entry to be replaced regardless of guard mismatches.
    /// </summary>
    /// <param name="file">The aggregate to project.</param>
    /// <param name="newContentHash">The new content hash to persist.</param>
    /// <param name="tokenHash">The analyzer token hash captured for the projection.</param>
    /// <param name="guard">The projection transaction guard.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ForceReplaceAsync(
        FileEntity file,
        string? newContentHash,
        string? tokenHash,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the search projection entry for the supplied identifier.
    /// </summary>
    /// <param name="fileId">The file identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task DeleteAsync(Guid fileId, CancellationToken cancellationToken);
}
