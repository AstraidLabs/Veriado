using Veriado.Appl.Abstractions;
using Veriado.Domain.Files;

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
        ConfigureDomainToDtoMappings();
        ConfigureReadModelMappings();
    }

    private void ConfigureDomainToDtoMappings()
    {
        CreateMap<FileDocumentValidityEntity, FileValidityDto>()
            .ForCtorParam(nameof(FileValidityDto.IssuedAt), opt => opt.MapFrom(static src => src.IssuedAt))
            .ForCtorParam(nameof(FileValidityDto.ValidUntil), opt => opt.MapFrom(static src => src.ValidUntil))
            .ForCtorParam(nameof(FileValidityDto.HasPhysicalCopy), opt => opt.MapFrom(static src => src.HasPhysicalCopy))
            .ForCtorParam(nameof(FileValidityDto.HasElectronicCopy), opt => opt.MapFrom(static src => src.HasElectronicCopy));

        CreateMap<FileSystemMetadata, FileSystemMetadataDto>()
            .ForCtorParam(nameof(FileSystemMetadataDto.Attributes), opt => opt.MapFrom(static src => src.Attributes))
            .ForCtorParam(nameof(FileSystemMetadataDto.CreatedUtc), opt => opt.MapFrom(static src => src.CreatedUtc))
            .ForCtorParam(nameof(FileSystemMetadataDto.LastWriteUtc), opt => opt.MapFrom(static src => src.LastWriteUtc))
            .ForCtorParam(nameof(FileSystemMetadataDto.LastAccessUtc), opt => opt.MapFrom(static src => src.LastAccessUtc))
            .ForCtorParam(nameof(FileSystemMetadataDto.OwnerSid), opt => opt.MapFrom(static src => src.OwnerSid))
            .ForCtorParam(nameof(FileSystemMetadataDto.HardLinkCount), opt => opt.MapFrom(static src => src.HardLinkCount))
            .ForCtorParam(nameof(FileSystemMetadataDto.AlternateDataStreamCount), opt =>
                opt.MapFrom(static src => src.AlternateDataStreamCount));

        CreateMap<FileContentEntity, FileContentDto>()
            .ForCtorParam(nameof(FileContentDto.Hash), opt => opt.MapFrom(static src => src.Hash))
            .ForCtorParam(nameof(FileContentDto.Length), opt => opt.MapFrom(static src => src.Length))
            .ForCtorParam(nameof(FileContentDto.Bytes), opt => opt.MapFrom(static _ => (byte[]?)null));

        CreateMap<FileEntity, FileContentResponseDto>()
            .ForCtorParam(nameof(FileContentResponseDto.Id), opt => opt.MapFrom(static src => src.Id))
            .ForCtorParam(nameof(FileContentResponseDto.Name), opt => opt.MapFrom(static src => src.Name))
            .ForCtorParam(nameof(FileContentResponseDto.Extension), opt => opt.MapFrom(static src => src.Extension))
            .ForCtorParam(nameof(FileContentResponseDto.Mime), opt => opt.MapFrom(static src => src.Mime))
            .ForCtorParam(nameof(FileContentResponseDto.Author), opt => opt.MapFrom(static src => src.Author))
            .ForCtorParam(nameof(FileContentResponseDto.SizeBytes), opt => opt.MapFrom(static src => src.Size))
            .ForCtorParam(nameof(FileContentResponseDto.Version), opt => opt.MapFrom(static src => src.Version))
            .ForCtorParam(nameof(FileContentResponseDto.IsReadOnly), opt => opt.MapFrom(static src => src.IsReadOnly))
            .ForCtorParam(nameof(FileContentResponseDto.CreatedUtc), opt => opt.MapFrom(static src => src.CreatedUtc))
            .ForCtorParam(nameof(FileContentResponseDto.LastModifiedUtc), opt => opt.MapFrom(static src => src.LastModifiedUtc))
            .ForCtorParam(nameof(FileContentResponseDto.Validity), opt => opt.MapFrom(static src => src.Validity))
            .ForCtorParam(nameof(FileContentResponseDto.Content), opt =>
                opt.MapFrom(static src => CloneBytes(src.Content.Bytes)));

        CreateMap<FileEntity, FileSummaryDto>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(static src => src.Name))
            .ForMember(dest => dest.Extension, opt => opt.MapFrom(static src => src.Extension))
            .ForMember(dest => dest.Mime, opt => opt.MapFrom(static src => src.Mime))
            .ForMember(dest => dest.Title, opt => opt.MapFrom(static src => src.Title ?? src.Name.Value))
            .ForMember(dest => dest.Author, opt => opt.MapFrom(static src => src.Author))
            .ForMember(dest => dest.Size, opt => opt.MapFrom(static src => src.Size))
            .ForMember(dest => dest.CreatedUtc, opt => opt.MapFrom(static src => src.CreatedUtc))
            .ForMember(dest => dest.LastModifiedUtc, opt => opt.MapFrom(static src => src.LastModifiedUtc))
            .ForMember(dest => dest.IsReadOnly, opt => opt.MapFrom(static src => src.IsReadOnly))
            .ForMember(dest => dest.Version, opt => opt.MapFrom(static src => src.Version))
            .ForMember(dest => dest.Validity, opt => opt.MapFrom(static src => src.Validity))
            .ForMember(dest => dest.IsIndexStale, ConfigureSearchIndexMember<FileSummaryDto, bool>(static state => state.IsStale))
            .ForMember(dest => dest.LastIndexedUtc, ConfigureSearchIndexMember<FileSummaryDto, DateTimeOffset?>(static state => state.LastIndexedUtc))
            .ForMember(dest => dest.IndexedTitle, ConfigureSearchIndexMember<FileSummaryDto, string?>(static state => state.IndexedTitle))
            .ForMember(dest => dest.IndexSchemaVersion, ConfigureSearchIndexMember<FileSummaryDto, int>(static state => state.SchemaVersion))
            .ForMember(dest => dest.IndexedContentHash, ConfigureSearchIndexMember<FileSummaryDto, string?>(static state => state.IndexedContentHash))
            .ForMember(dest => dest.Score, opt => opt.Ignore());

        CreateMap<FileEntity, FileDetailDto>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(static src => src.Name))
            .ForMember(dest => dest.Extension, opt => opt.MapFrom(static src => src.Extension))
            .ForMember(dest => dest.Mime, opt => opt.MapFrom(static src => src.Mime))
            .ForMember(dest => dest.Author, opt => opt.MapFrom(static src => src.Author))
            .ForMember(dest => dest.Title, opt => opt.MapFrom(static src => src.Title ?? src.Name.Value))
            .ForMember(dest => dest.Size, opt => opt.MapFrom(static src => src.Size))
            .ForMember(dest => dest.CreatedUtc, opt => opt.MapFrom(static src => src.CreatedUtc))
            .ForMember(dest => dest.LastModifiedUtc, opt => opt.MapFrom(static src => src.LastModifiedUtc))
            .ForMember(dest => dest.IsReadOnly, opt => opt.MapFrom(static src => src.IsReadOnly))
            .ForMember(dest => dest.Version, opt => opt.MapFrom(static src => src.Version))
            .ForMember(dest => dest.Content, opt => opt.MapFrom(static src => src.Content))
            .ForMember(dest => dest.SystemMetadata, opt => opt.MapFrom(static src => src.SystemMetadata))
            .ForMember(dest => dest.Validity, opt => opt.MapFrom(static src => src.Validity))
            .ForMember(dest => dest.IsIndexStale, ConfigureSearchIndexMember<FileDetailDto, bool>(static state => state.IsStale))
            .ForMember(dest => dest.LastIndexedUtc, ConfigureSearchIndexMember<FileDetailDto, DateTimeOffset?>(static state => state.LastIndexedUtc))
            .ForMember(dest => dest.IndexedTitle, ConfigureSearchIndexMember<FileDetailDto, string?>(static state => state.IndexedTitle))
            .ForMember(dest => dest.IndexSchemaVersion, ConfigureSearchIndexMember<FileDetailDto, int>(static state => state.SchemaVersion))
            .ForMember(dest => dest.IndexedContentHash, ConfigureSearchIndexMember<FileDetailDto, string?>(static state => state.IndexedContentHash));
    }

    private void ConfigureReadModelMappings()
    {
        CreateMap<FileDocumentValidityReadModel, FileValidityDto>()
            .ForCtorParam(nameof(FileValidityDto.IssuedAt), opt => opt.MapFrom(static src => src.IssuedAtUtc))
            .ForCtorParam(nameof(FileValidityDto.ValidUntil), opt => opt.MapFrom(static src => src.ValidUntilUtc))
            .ForCtorParam(nameof(FileValidityDto.HasPhysicalCopy), opt => opt.MapFrom(static src => src.HasPhysicalCopy))
            .ForCtorParam(nameof(FileValidityDto.HasElectronicCopy), opt => opt.MapFrom(static src => src.HasElectronicCopy));

        CreateMap<FileListItemReadModel, FileListItemDto>();

        CreateMap<FileDetailReadModel, FileDetailDto>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(static src => src.Name))
            .ForMember(dest => dest.Extension, opt => opt.MapFrom(static src => src.Extension))
            .ForMember(dest => dest.Mime, opt => opt.MapFrom(static src => src.Mime))
            .ForMember(dest => dest.Author, opt => opt.MapFrom(static src => src.Author))
            .ForMember(dest => dest.Size, opt => opt.MapFrom(static src => src.SizeBytes))
            .ForMember(dest => dest.CreatedUtc, opt => opt.MapFrom(static src => src.CreatedUtc))
            .ForMember(dest => dest.LastModifiedUtc, opt => opt.MapFrom(static src => src.LastModifiedUtc))
            .ForMember(dest => dest.IsReadOnly, opt => opt.MapFrom(static src => src.IsReadOnly))
            .ForMember(dest => dest.Version, opt => opt.MapFrom(static src => src.Version))
            .ForMember(dest => dest.Content, opt =>
                opt.MapFrom(static src => new FileContentDto(string.Empty, src.SizeBytes, null)))
            .ForMember(dest => dest.SystemMetadata, opt => opt.MapFrom(static src => src.SystemMetadata))
            .ForMember(dest => dest.Validity, opt => opt.MapFrom(static src => src.Validity))
            .ForMember(dest => dest.IsIndexStale, opt => opt.MapFrom(static _ => false))
            .ForMember(dest => dest.LastIndexedUtc, opt => opt.MapFrom(static _ => (DateTimeOffset?)null))
            .ForMember(dest => dest.IndexedTitle, opt => opt.MapFrom(static _ => (string?)null))
            .ForMember(dest => dest.IndexSchemaVersion, opt => opt.MapFrom(static _ => 0))
            .ForMember(dest => dest.IndexedContentHash, opt => opt.MapFrom(static _ => (string?)null));
    }

    private static Action<IMemberConfigurationExpression<FileEntity, TDestination, TValue>> ConfigureSearchIndexMember<TDestination, TValue>(Func<SearchIndexState, TValue> selector)
    {
        return opt =>
        {
            opt.PreCondition(static src => src.SearchIndex is not null);
            opt.MapFrom(src => selector(src.SearchIndex!));
        };
    }

    private static byte[] CloneBytes(byte[] source)
    {
        if (source is null || source.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var clone = new byte[source.Length];
        Array.Copy(source, clone, source.Length);
        return clone;
    }
}
