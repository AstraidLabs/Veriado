using System;

namespace Veriado.Appl.FileSystem;

/// <summary>
/// Provides coordination between physical file system state changes and logical file aggregates.
/// </summary>
public interface IFileSystemSyncService
{
    /// <summary>
    /// Handles the case where the physical file is missing.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the <see cref="Veriado.Domain.FileSystem.FileSystemEntity"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task HandleFileMissingAsync(Guid fileSystemId, CancellationToken cancellationToken);

    /// <summary>
    /// Handles the case where a previously missing physical file has been restored.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the <see cref="Veriado.Domain.FileSystem.FileSystemEntity"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task HandleFileRehydratedAsync(Guid fileSystemId, CancellationToken cancellationToken);

    /// <summary>
    /// Handles updates when the physical file has moved or been renamed.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the <see cref="Veriado.Domain.FileSystem.FileSystemEntity"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task HandleFileMovedAsync(Guid fileSystemId, CancellationToken cancellationToken);

    /// <summary>
    /// Handles updates when the physical file content has changed.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the <see cref="Veriado.Domain.FileSystem.FileSystemEntity"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task HandleFileContentChangedAsync(Guid fileSystemId, CancellationToken cancellationToken);
}
