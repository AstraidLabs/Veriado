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

namespace Veriado.Appl.UseCases.Files.SetExtendedMetadata;

/// <summary>
/// Handles setting extended metadata values for files.
/// </summary>
public sealed class SetExtendedMetadataHandler : FileWriteHandlerBase, IRequestHandler<SetExtendedMetadataCommand, AppResult<FileSummaryDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SetExtendedMetadataHandler"/> class.
    /// </summary>
    public SetExtendedMetadataHandler(IFileRepository repository, IClock clock, IMapper mapper)
        : base(repository, clock, mapper)
    {
    }

    /// <inheritdoc />
    public async Task<AppResult<FileSummaryDto>> Handle(SetExtendedMetadataCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Entries is null || request.Entries.Count == 0)
            {
                return AppResult<FileSummaryDto>.Validation(new[] { "At least one metadata entry must be provided." });
            }

            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileSummaryDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var timestamp = CurrentTimestamp();
            file.SetExtendedMetadata(timestamp, builder =>
            {
                foreach (var entry in request.Entries)
                {
                    var key = new PropertyKey(entry.FormatId, entry.PropertyId);
                    if (entry.Value is null)
                    {
                        builder.Remove(key);
                    }
                    else
                    {
                        builder.Set(key, entry.Value.Value);
                    }
                }
            });

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
            return AppResult<FileSummaryDto>.FromException(ex, "Failed to set extended metadata.");
        }
    }

}
