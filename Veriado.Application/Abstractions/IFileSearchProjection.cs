namespace Veriado.Appl.Abstractions;

/// <summary>
/// Provides synchronous projection helpers for file aggregates into the search index.
/// </summary>
public interface IFileSearchProjection
{
    /// <summary>
    /// Upserts the specified file aggregate into the search projection store.
    /// </summary>
    /// <param name="file">The aggregate to project.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UpsertAsync(FileEntity file, ISearchProjectionTransactionGuard guard, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the search projection entry for the supplied identifier.
    /// </summary>
    /// <param name="fileId">The file identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task DeleteAsync(Guid fileId, ISearchProjectionTransactionGuard guard, CancellationToken cancellationToken);
}
