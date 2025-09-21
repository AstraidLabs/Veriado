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

namespace Veriado.Application.UseCases.Files.SetExtendedMetadata;

/// <summary>
/// Handles setting extended metadata values for files.
/// </summary>
public sealed class SetExtendedMetadataHandler : FileWriteHandlerBase, IRequestHandler<SetExtendedMetadataCommand, AppResult<FileDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SetExtendedMetadataHandler"/> class.
    /// </summary>
    public SetExtendedMetadataHandler(IFileRepository repository)
        : base(repository)
    {
    }

    /// <inheritdoc />
    public async Task<AppResult<FileDto>> Handle(SetExtendedMetadataCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Entries is null || request.Entries.Count == 0)
            {
                return AppResult<FileDto>.Validation(new[] { "At least one metadata entry must be provided." });
            }

            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            file.SetExtendedMetadata(builder =>
            {
                foreach (var entry in request.Entries)
                {
                    var key = new PropertyKey(entry.FormatId, entry.PropertyId);
                    if (string.IsNullOrWhiteSpace(entry.Value))
                    {
                        builder.Remove(key);
                    }
                    else
                    {
                        builder.Set(key, MetadataValue.FromString(entry.Value));
                    }
                }
            });

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
            return AppResult<FileDto>.FromException(ex, "Failed to set extended metadata.");
        }
    }

}
