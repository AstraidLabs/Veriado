using System;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Import.Models;

namespace Veriado.Services.Import;

/// <summary>
/// Provides high-level orchestration for importing files and folders through the application layer.
/// </summary>
public interface IImportService
{
    /// <summary>
    /// Imports a single file described by the supplied request contract.
    /// </summary>
    /// <param name="request">The create-file request payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An API response containing the created file identifier.</returns>
    Task<ApiResponse<Guid>> ImportFileAsync(CreateFileRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Imports all files contained in the specified folder using the provided options.
    /// </summary>
    /// <param name="request">The folder import request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An API response containing the aggregated batch result.</returns>
    Task<ApiResponse<ImportBatchResult>> ImportFolderAsync(ImportFolderRequest request, CancellationToken cancellationToken);
}
