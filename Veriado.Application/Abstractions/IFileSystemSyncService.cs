namespace Veriado.Appl.Abstractions;

/// <summary>
/// Coordinates reactions to changes in physical file system entities and their logical representations.
/// </summary>
public interface IFileSystemSyncService
{
    /// <summary>
    /// Handles cases where the physical file is missing from storage.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the file system entity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task HandleFileMissingAsync(Guid fileSystemId, CancellationToken cancellationToken);

    /// <summary>
    /// Handles cases where a previously missing physical file has been restored.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the file system entity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task HandleFileRehydratedAsync(Guid fileSystemId, CancellationToken cancellationToken);

    /// <summary>
    /// Handles cases where the physical file location has changed.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the file system entity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task HandleFileMovedAsync(Guid fileSystemId, CancellationToken cancellationToken);

    /// <summary>
    /// Handles cases where the contents of the physical file differ from what is tracked.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the file system entity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task HandleFileContentChangedAsync(Guid fileSystemId, CancellationToken cancellationToken);
}
