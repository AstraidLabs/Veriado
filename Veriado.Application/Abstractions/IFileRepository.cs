using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Files;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Defines write-oriented persistence operations for <see cref="FileEntity"/> aggregates.
/// </summary>
public interface IFileRepository
{
    /// <summary>
    /// Gets a file aggregate by its identifier.
    /// </summary>
    /// <param name="id">The file identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The aggregate or <see langword="null"/> if not found.</returns>
    Task<FileEntity?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a collection of file aggregates by their identifiers.
    /// </summary>
    /// <param name="ids">The file identifiers to retrieve.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The collection of aggregates found for the provided identifiers.</returns>
    Task<IReadOnlyList<FileEntity>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Streams all file aggregates.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An asynchronous sequence of files.</returns>
    IAsyncEnumerable<FileEntity> StreamAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new file aggregate to the store.
    /// </summary>
    /// <param name="file">The aggregate to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(FileEntity file, CancellationToken cancellationToken);

    /// <summary>
    /// Persists changes to an existing aggregate.
    /// </summary>
    /// <param name="file">The aggregate to update.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UpdateAsync(FileEntity file, CancellationToken cancellationToken);
}
