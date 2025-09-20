using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Files;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Provides access to persisted file aggregates.
/// </summary>
public interface IFileRepository
{
    /// <summary>
    /// Loads a file aggregate by its identifier.
    /// </summary>
    /// <param name="id">The identifier of the file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The loaded aggregate or <see langword="null"/> when it does not exist.</returns>
    Task<FileEntity?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Loads multiple file aggregates by their identifiers in a single call.
    /// </summary>
    /// <param name="ids">The identifiers of the files to retrieve.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The loaded aggregates. Missing identifiers are omitted from the result.</returns>
    Task<IReadOnlyList<FileEntity>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Streams all file aggregates from the persistence store.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An asynchronous sequence of file aggregates.</returns>
    IAsyncEnumerable<FileEntity> StreamAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds a newly created file aggregate to the persistence store.
    /// </summary>
    /// <param name="file">The aggregate to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(FileEntity file, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the provided file aggregate updates.
    /// </summary>
    /// <param name="file">The aggregate to persist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UpdateAsync(FileEntity file, CancellationToken cancellationToken);
}
