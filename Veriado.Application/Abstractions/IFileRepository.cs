namespace Veriado.Appl.Abstractions;

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
    /// Loads a file system aggregate by its identifier.
    /// </summary>
    /// <param name="id">The identifier of the file system entity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The loaded aggregate or <see langword="null"/> when it does not exist.</returns>
    Task<FileSystemEntity?> GetFileSystemAsync(Guid id, CancellationToken cancellationToken);

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
    /// Checks whether a file with the specified content hash already exists.
    /// </summary>
    /// <param name="hash">The content hash to look up.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when a file with the given hash exists; otherwise <see langword="false"/>.</returns>
    Task<bool> ExistsByHashAsync(FileHash hash, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a newly created file aggregate to the persistence store.
    /// </summary>
    /// <param name="file">The aggregate to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(FileEntity file, FilePersistenceOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a file aggregate together with its backing file system entity.
    /// </summary>
    /// <param name="file">The file aggregate.</param>
    /// <param name="fileSystem">The file system aggregate describing the stored content.</param>
    /// <param name="options">The persistence options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(FileEntity file, FileSystemEntity fileSystem, FilePersistenceOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the provided file aggregate updates.
    /// </summary>
    /// <param name="file">The aggregate to persist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UpdateAsync(FileEntity file, FilePersistenceOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Persists updates to a file aggregate and its associated file system entity.
    /// </summary>
    /// <param name="file">The file aggregate.</param>
    /// <param name="fileSystem">The associated file system aggregate.</param>
    /// <param name="options">The persistence options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UpdateAsync(FileEntity file, FileSystemEntity fileSystem, FilePersistenceOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the file aggregate with the provided identifier.
    /// </summary>
    /// <param name="id">The identifier of the file to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

}
