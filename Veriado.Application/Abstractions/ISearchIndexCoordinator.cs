using System.Data.Common;

namespace Veriado.Appl.Abstractions;

/// <summary>
/// Coordinates search indexing operations taking the configured infrastructure mode into account.
/// </summary>
public interface ISearchIndexCoordinator
{
    /// <summary>
    /// Executes the indexing pipeline for the provided file aggregate.
    /// </summary>
    /// <param name="file">The file aggregate to index.</param>
    /// <param name="options">The persistence options supplied by the application layer.</param>
    /// <param name="transaction">The ambient database transaction, if any.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the document was indexed immediately; otherwise <see langword="false"/>.</returns>
    Task<bool> IndexAsync(FileEntity file, FilePersistenceOptions options, DbTransaction? transaction, CancellationToken cancellationToken);

    #region TODO(SQLiteOnly): Remove deferred indexing refresh once outbox pipeline is removed
    /// <summary>
    /// Forces any deferred indexing work to be processed immediately.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task SearchIndexRefreshAsync(CancellationToken cancellationToken);
    #endregion
}
