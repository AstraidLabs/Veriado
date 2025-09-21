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
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Application.UseCases.Files.ApplySystemMetadata;

/// <summary>
/// Handles applying system metadata snapshots to files.
/// </summary>
public sealed class ApplySystemMetadataHandler : FileWriteHandlerBase, IRequestHandler<ApplySystemMetadataCommand, AppResult<FileDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApplySystemMetadataHandler"/> class.
    /// </summary>
    public ApplySystemMetadataHandler(IFileRepository repository)
        : base(repository)
    {
    }

    /// <inheritdoc />
    public async Task<AppResult<FileDto>> Handle(ApplySystemMetadataCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var metadata = new FileSystemMetadata(
                request.Attributes,
                UtcTimestamp.From(request.CreatedUtc),
                UtcTimestamp.From(request.LastWriteUtc),
                UtcTimestamp.From(request.LastAccessUtc),
                request.OwnerSid,
                request.HardLinkCount,
                request.AlternateDataStreamCount);

            file.ApplySystemMetadata(metadata);
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
            return AppResult<FileDto>.FromException(ex, "Failed to apply system metadata.");
        }
    }

}
