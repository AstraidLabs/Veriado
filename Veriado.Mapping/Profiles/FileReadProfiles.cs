using AutoMapper;
using Veriado.Appl.Abstractions;
using Veriado.Contracts.Files;
using Veriado.Domain.Files;
using Veriado.Domain.Metadata;

namespace Veriado.Mapping.Profiles;

/// <summary>
/// Configures mappings used when projecting domain entities and read models to read DTOs.
/// </summary>
public sealed class FileReadProfiles : Profile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileReadProfiles"/> class.
    /// </summary>
    public FileReadProfiles()
    {
        CreateMap<FileDocumentValidityEntity, FileValidityDto>()
            .ForCtorParam(nameof(FileValidityDto.IssuedAt), opt => opt.MapFrom(src => src.IssuedAt))
            .ForCtorParam(nameof(FileValidityDto.ValidUntil), opt => opt.MapFrom(src => src.ValidUntil))
            .ForCtorParam(nameof(FileValidityDto.HasPhysicalCopy), opt => opt.MapFrom(src => src.HasPhysicalCopy))
            .ForCtorParam(nameof(FileValidityDto.HasElectronicCopy), opt => opt.MapFrom(src => src.HasElectronicCopy));

        CreateMap<FileSystemMetadata, FileSystemMetadataDto>()
            .ForCtorParam(nameof(FileSystemMetadataDto.Attributes), opt => opt.MapFrom(src => src.Attributes))
            .ForCtorParam(nameof(FileSystemMetadataDto.CreatedUtc), opt => opt.MapFrom(src => src.CreatedUtc))
            .ForCtorParam(nameof(FileSystemMetadataDto.LastWriteUtc), opt => opt.MapFrom(src => src.LastWriteUtc))
            .ForCtorParam(nameof(FileSystemMetadataDto.LastAccessUtc), opt => opt.MapFrom(src => src.LastAccessUtc))
            .ForCtorParam(nameof(FileSystemMetadataDto.OwnerSid), opt => opt.MapFrom(src => src.OwnerSid))
            .ForCtorParam(nameof(FileSystemMetadataDto.HardLinkCount), opt => opt.MapFrom(src => src.HardLinkCount))
            .ForCtorParam(nameof(FileSystemMetadataDto.AlternateDataStreamCount), opt => opt.MapFrom(src => src.AlternateDataStreamCount));

        CreateMap<FileContentEntity, FileContentDto>()
            .ForCtorParam(nameof(FileContentDto.Hash), opt => opt.MapFrom(src => src.Hash))
            .ForCtorParam(nameof(FileContentDto.Length), opt => opt.MapFrom(src => src.Length))
            .ForCtorParam(nameof(FileContentDto.Bytes), opt => opt.MapFrom(_ => (byte[]?)null));

        CreateMap<FileDocumentValidityReadModel, FileValidityDto>()
            .ForCtorParam(nameof(FileValidityDto.IssuedAt), opt => opt.MapFrom(src => src.IssuedAtUtc))
            .ForCtorParam(nameof(FileValidityDto.ValidUntil), opt => opt.MapFrom(src => src.ValidUntilUtc))
            .ForCtorParam(nameof(FileValidityDto.HasPhysicalCopy), opt => opt.MapFrom(src => src.HasPhysicalCopy))
            .ForCtorParam(nameof(FileValidityDto.HasElectronicCopy), opt => opt.MapFrom(src => src.HasElectronicCopy));

        CreateMap<FileListItemReadModel, FileListItemDto>();

        CreateMap<FileEntity, FileSummaryDto>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
            .ForMember(dest => dest.Extension, opt => opt.MapFrom(src => src.Extension))
            .ForMember(dest => dest.Mime, opt => opt.MapFrom(src => src.Mime))
            .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Title ?? src.Name.Value))
            .ForMember(dest => dest.Size, opt => opt.MapFrom(src => src.Size))
            .ForMember(dest => dest.CreatedUtc, opt => opt.MapFrom(src => src.CreatedUtc))
            .ForMember(dest => dest.LastModifiedUtc, opt => opt.MapFrom(src => src.LastModifiedUtc))
            .ForMember(dest => dest.Validity, opt => opt.MapFrom(src => src.Validity))
            // Search index state is optional; guard members to avoid null dereferences and preserve defaults.
            .ForMember(dest => dest.IsIndexStale, opt =>
            {
                opt.PreCondition(src => src.SearchIndex is not null);
                opt.MapFrom(src => src.SearchIndex!.IsStale);
            })
            .ForMember(dest => dest.LastIndexedUtc, opt =>
            {
                opt.PreCondition(src => src.SearchIndex is not null);
                opt.MapFrom(src => src.SearchIndex!.LastIndexedUtc);
            })
            .ForMember(dest => dest.IndexedTitle, opt =>
            {
                opt.PreCondition(src => src.SearchIndex is not null);
                opt.MapFrom(src => src.SearchIndex!.IndexedTitle);
            })
            .ForMember(dest => dest.IndexSchemaVersion, opt =>
            {
                opt.PreCondition(src => src.SearchIndex is not null);
                opt.MapFrom(src => src.SearchIndex!.SchemaVersion);
            })
            .ForMember(dest => dest.IndexedContentHash, opt =>
            {
                opt.PreCondition(src => src.SearchIndex is not null);
                opt.MapFrom(src => src.SearchIndex!.IndexedContentHash);
            })
            .ForMember(dest => dest.Score, opt => opt.Ignore());

        CreateMap<FileEntity, FileDetailDto>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
            .ForMember(dest => dest.Extension, opt => opt.MapFrom(src => src.Extension))
            .ForMember(dest => dest.Mime, opt => opt.MapFrom(src => src.Mime))
            .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Title ?? src.Name.Value))
            .ForMember(dest => dest.Size, opt => opt.MapFrom(src => src.Size))
            .ForMember(dest => dest.CreatedUtc, opt => opt.MapFrom(src => src.CreatedUtc))
            .ForMember(dest => dest.LastModifiedUtc, opt => opt.MapFrom(src => src.LastModifiedUtc))
            .ForMember(dest => dest.Content, opt => opt.MapFrom(src => src.Content))
            .ForMember(dest => dest.SystemMetadata, opt => opt.MapFrom(src => src.SystemMetadata))
            .ForMember(dest => dest.Validity, opt => opt.MapFrom(src => src.Validity))
            // Avoid forcing default values when search indexing has not run yet.
            .ForMember(dest => dest.IsIndexStale, opt =>
            {
                opt.PreCondition(src => src.SearchIndex is not null);
                opt.MapFrom(src => src.SearchIndex!.IsStale);
            })
            .ForMember(dest => dest.LastIndexedUtc, opt =>
            {
                opt.PreCondition(src => src.SearchIndex is not null);
                opt.MapFrom(src => src.SearchIndex!.LastIndexedUtc);
            })
            .ForMember(dest => dest.IndexedTitle, opt =>
            {
                opt.PreCondition(src => src.SearchIndex is not null);
                opt.MapFrom(src => src.SearchIndex!.IndexedTitle);
            })
            .ForMember(dest => dest.IndexSchemaVersion, opt =>
            {
                opt.PreCondition(src => src.SearchIndex is not null);
                opt.MapFrom(src => src.SearchIndex!.SchemaVersion);
            })
            .ForMember(dest => dest.IndexedContentHash, opt =>
            {
                opt.PreCondition(src => src.SearchIndex is not null);
                opt.MapFrom(src => src.SearchIndex!.IndexedContentHash);
            });

        CreateMap<FileDetailReadModel, FileDetailDto>()
            .ForMember(dest => dest.Size, opt => opt.MapFrom(src => src.SizeBytes))
            // The read model does not surface hashes; expose metadata only.
            .ForMember(dest => dest.Content, opt => opt.MapFrom(src => new FileContentDto(string.Empty, src.SizeBytes)))
            .ForMember(dest => dest.SystemMetadata, opt => opt.MapFrom(src => src.SystemMetadata))
            .ForMember(dest => dest.Validity, opt => opt.MapFrom(src => src.Validity));
    }
}
