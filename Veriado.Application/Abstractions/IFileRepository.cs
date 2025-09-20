using System;
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
