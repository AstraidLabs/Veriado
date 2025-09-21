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

namespace Veriado.Application.UseCases.Files.UpdateFileMetadata;

/// <summary>
/// Handles updates of core file metadata.
/// </summary>
public sealed class UpdateFileMetadataHandler : FileWriteHandlerBase, IRequestHandler<UpdateFileMetadataCommand, AppResult<FileDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateFileMetadataHandler"/> class.
    /// </summary>
    public UpdateFileMetadataHandler(
        IFileRepository repository,
        IEventPublisher eventPublisher,
        ISearchIndexCoordinator indexCoordinator,
        IClock clock)
        : base(repository, eventPublisher, indexCoordinator, clock)
    {
    }

    /// <inheritdoc />
    public async Task<AppResult<FileDto>> Handle(UpdateFileMetadataCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            MimeType? mime = request.Mime is null ? null : MimeType.From(request.Mime);
            file.UpdateMetadata(mime, request.Author);
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
            return AppResult<FileDto>.FromException(ex, "Failed to update file metadata.");
        }
    }

}
