using System;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Common;
using Veriado.Application.DTO;

namespace Veriado.Services.Files;

/// <summary>
/// Provides helpers for accessing and persisting file binary content.
/// </summary>
public interface IFileContentService
{
    Task<(FileDto Meta, byte[] Content)?> GetContentAsync(Guid fileId, CancellationToken cancellationToken);

    Task<AppResult<Guid>> SaveContentToDiskAsync(Guid fileId, string targetPath, CancellationToken cancellationToken);
}
