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

namespace Veriado.Application.UseCases.Files.SetFileValidity;

/// <summary>
/// Handles updates to document validity metadata for files.
/// </summary>
public sealed class SetFileValidityHandler : FileWriteHandlerBase, IRequestHandler<SetFileValidityCommand, AppResult<FileDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SetFileValidityHandler"/> class.
    /// </summary>
    public SetFileValidityHandler(IFileRepository repository)
        : base(repository)
    {
    }

    /// <inheritdoc />
    public async Task<AppResult<FileDto>> Handle(SetFileValidityCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var issued = UtcTimestamp.From(request.IssuedAtUtc);
            var validUntil = UtcTimestamp.From(request.ValidUntilUtc);
            file.SetValidity(issued, validUntil, request.HasPhysicalCopy, request.HasElectronicCopy);
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
            return AppResult<FileDto>.FromException(ex, "Failed to update file validity.");
        }
    }

}
