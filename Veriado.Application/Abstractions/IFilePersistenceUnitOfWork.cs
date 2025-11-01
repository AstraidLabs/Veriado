namespace Veriado.Appl.Abstractions;

/// <summary>
/// Represents the write-side persistence unit of work for file aggregates.
/// </summary>
public interface IFilePersistenceUnitOfWork
{
    /// <summary>
    /// Gets a value indicating whether the underlying persistence context is tracking changes.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<bool> HasTrackedChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Begins a new transactional scope for subsequent operations.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IFilePersistenceTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists pending changes to the underlying store.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Represents a transactional scope created by <see cref="IFilePersistenceUnitOfWork"/>.
/// </summary>
public interface IFilePersistenceTransaction : IAsyncDisposable
{
    /// <summary>
    /// Commits the transaction.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task CommitAsync(CancellationToken cancellationToken);
}
