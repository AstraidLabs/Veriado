using System;
using Veriado.Contracts.Import;

namespace Veriado.Services.Import;

/// <summary>
/// Provides high-level orchestration for importing files and folders through the application layer.
/// </summary>
public interface IImportService
{
    event EventHandler<ImportLifecycleFallbackEventArgs>? LifecycleFallback;

    /// <summary>
    /// Imports a single file described by the supplied request contract.
    /// </summary>
    /// <param name="request">The create-file request payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An API response containing the created file identifier.</returns>
    [Obsolete("Use the streaming import APIs.")]
    Task<ApiResponse<Guid>> ImportFileAsync(CreateFileRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Imports all files contained in the specified folder using the provided options.
    /// </summary>
    /// <param name="request">The folder import request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An API response containing the aggregated batch result.</returns>
    [Obsolete("Use ImportFolderStreamAsync instead.")]
    Task<ApiResponse<ImportBatchResult>> ImportFolderAsync(ImportFolderRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Streams strongly typed progress events while importing all files in the specified folder.
    /// </summary>
    /// <param name="folderPath">The absolute folder path to import.</param>
    /// <param name="options">Optional import options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An asynchronous stream of import progress events.</returns>
    IAsyncEnumerable<ImportProgressEvent> ImportFolderStreamAsync(
        string folderPath,
        ImportOptions? options,
        CancellationToken cancellationToken);
}

public sealed record ImportLifecycleFallbackEventArgs(
    int RequestedParallelism,
    int EffectiveParallelism,
    int Attempts,
    DateTimeOffset OccurredUtc,
    string Message);
