using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Application.DTO;
using Veriado.Application.Mapping;
using Veriado.Domain.Files;

namespace Veriado.Application.UseCases.Maintenance;

/// <summary>
/// Handles explicit reindexing requests for a file.
/// </summary>
public sealed class ReindexFileHandler : IRequestHandler<ReindexFileCommand, AppResult<FileDto>>
{
    private readonly IFileRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReindexFileHandler"/> class.
    /// </summary>
    public ReindexFileHandler(IFileRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<AppResult<FileDto>> Handle(ReindexFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await _repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            file.RequestManualReindex();
            await _repository.UpdateAsync(file, cancellationToken).ConfigureAwait(false);
            return AppResult<FileDto>.Success(DomainToDto.ToFileDto(file));
        }
        catch (Exception ex)
        {
            return AppResult<FileDto>.FromException(ex, "Failed to reindex the file.");
        }
    }
}
