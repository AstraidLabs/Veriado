using System;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Common;
using Veriado.Contracts.Files;

namespace Veriado.Services.Files;

/// <summary>
/// Provides orchestration helpers for file mutations.
/// </summary>
public interface IFileOperationsService
{
    Task<AppResult<Guid>> RenameAsync(Guid fileId, string newName, CancellationToken cancellationToken);

    Task<AppResult<Guid>> UpdateMetadataAsync(UpdateMetadataRequest request, CancellationToken cancellationToken);

    Task<AppResult<Guid>> SetReadOnlyAsync(Guid fileId, bool isReadOnly, CancellationToken cancellationToken);

    Task<AppResult<Guid>> SetValidityAsync(Guid fileId, FileValidityDto validity, CancellationToken cancellationToken);

    Task<AppResult<Guid>> ClearValidityAsync(Guid fileId, CancellationToken cancellationToken);

    Task<AppResult<Guid>> ReplaceContentAsync(Guid fileId, byte[] content, bool extractContent, CancellationToken cancellationToken);

    Task<AppResult<Guid>> ApplySystemMetadataAsync(Guid fileId, FileSystemMetadataDto metadata, CancellationToken cancellationToken);
}
