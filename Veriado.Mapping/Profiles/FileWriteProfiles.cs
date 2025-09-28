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
        CreateMap<FileSystemMetadataDto, FileSystemMetadata>()
            .ForCtorParam("attributes", opt => opt.MapFrom(src => src.Attributes))
            .ForCtorParam("createdUtc", opt => opt.MapFrom(src => src.CreatedUtc))
            .ForCtorParam("lastWriteUtc", opt => opt.MapFrom(src => src.LastWriteUtc))
            .ForCtorParam("lastAccessUtc", opt => opt.MapFrom(src => src.LastAccessUtc))
            .ForCtorParam("ownerSid", opt => opt.MapFrom(src => src.OwnerSid))
            .ForCtorParam("hardLinkCount", opt => opt.MapFrom(src => src.HardLinkCount))
            .ForCtorParam("alternateDataStreamCount", opt => opt.MapFrom(src => src.AlternateDataStreamCount));
    }
}
