using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Common;
using Veriado.Appl.UseCases.Files.Common;
using Veriado.Contracts.Files;
using Veriado.Domain.Files;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Appl.UseCases.Files.ApplySystemMetadata;

/// <summary>
/// Handles applying system metadata snapshots to files.
/// </summary>
public sealed class ApplySystemMetadataHandler : FileWriteHandlerBase, IRequestHandler<ApplySystemMetadataCommand, AppResult<FileSummaryDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApplySystemMetadataHandler"/> class.
    /// </summary>
    public ApplySystemMetadataHandler(IFileRepository repository, IClock clock, IMapper mapper)
        : base(repository, clock, mapper)
    {
    }

    /// <inheritdoc />
    public async Task<AppResult<FileSummaryDto>> Handle(ApplySystemMetadataCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileSummaryDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var metadata = new FileSystemMetadata(
                request.Attributes,
                UtcTimestamp.From(request.CreatedUtc),
                UtcTimestamp.From(request.LastWriteUtc),
                UtcTimestamp.From(request.LastAccessUtc),
                request.OwnerSid,
                request.HardLinkCount,
                request.AlternateDataStreamCount);

            var timestamp = CurrentTimestamp();
            file.ApplySystemMetadata(metadata, timestamp);
            await PersistAsync(file, FilePersistenceOptions.Default, cancellationToken);
            return AppResult<FileSummaryDto>.Success(Mapper.Map<FileSummaryDto>(file));
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return AppResult<FileSummaryDto>.FromException(ex);
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<FileSummaryDto>.FromException(ex);
        }
        catch (Exception ex)
        {
            return AppResult<FileSummaryDto>.FromException(ex, "Failed to apply system metadata.");
        }
    }

}
