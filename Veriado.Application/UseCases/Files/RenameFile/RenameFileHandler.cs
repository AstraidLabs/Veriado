using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Application.DTO;
using Veriado.Application.Mapping;
using Veriado.Application.UseCases.Files.Common;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;

namespace Veriado.Application.UseCases.Files.RenameFile;

/// <summary>
/// Handles renaming file aggregates.
/// </summary>
public sealed class RenameFileHandler : FileWriteHandlerBase, IRequestHandler<RenameFileCommand, AppResult<FileDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RenameFileHandler"/> class.
    /// </summary>
    public RenameFileHandler(IFileRepository repository)
        : base(repository)
    {
    }

    /// <inheritdoc />
    public async Task<AppResult<FileDto>> Handle(RenameFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var newName = FileName.From(request.Name);
            file.Rename(newName);
            await PersistAsync(file, cancellationToken);
            return AppResult<FileDto>.Success(DomainToDto.ToFileDto(file));
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return AppResult<FileDto>.FromException(ex);
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<FileDto>.FromException(ex);
        }
        catch (Exception ex)
        {
            return AppResult<FileDto>.FromException(ex, "Failed to rename file.");
        }
    }

}
