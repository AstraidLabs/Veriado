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

namespace Veriado.Application.UseCases.Files.SetFileReadOnly;

/// <summary>
/// Handles toggling the read-only status of a file aggregate.
/// </summary>
public sealed class SetFileReadOnlyHandler : FileWriteHandlerBase, IRequestHandler<SetFileReadOnlyCommand, AppResult<FileDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SetFileReadOnlyHandler"/> class.
    /// </summary>
    public SetFileReadOnlyHandler(IFileRepository repository)
        : base(repository)
    {
    }

    /// <inheritdoc />
    public async Task<AppResult<FileDto>> Handle(SetFileReadOnlyCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            file.SetReadOnly(request.IsReadOnly);
            await PersistAsync(file, cancellationToken);
            return AppResult<FileDto>.Success(DomainToDto.ToFileDto(file));
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<FileDto>.FromException(ex);
        }
        catch (Exception ex)
        {
            return AppResult<FileDto>.FromException(ex, "Failed to change the read-only status.");
        }
    }

}
