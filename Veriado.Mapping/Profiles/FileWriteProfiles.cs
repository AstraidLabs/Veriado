using AutoMapper;
using Veriado.Contracts.Files;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Mapping.Profiles;

/// <summary>
/// Configures mappings used when converting write DTOs to domain constructs.
/// </summary>
public sealed class FileWriteProfiles : Profile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileWriteProfiles"/> class.
    /// </summary>
    public FileWriteProfiles()
    {
        CreateMap<FileSystemMetadataDto, FileSystemMetadata>().ConvertUsing(dto => new FileSystemMetadata(
            (FileAttributesFlags)dto.Attributes,
            UtcTimestamp.From(dto.CreatedUtc),
            UtcTimestamp.From(dto.LastWriteUtc),
            UtcTimestamp.From(dto.LastAccessUtc),
            dto.OwnerSid,
            dto.HardLinkCount,
            dto.AlternateDataStreamCount));
    }
}
