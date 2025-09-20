using System;
using System.Linq;
using AutoMapper;
using Veriado.Contracts.Files;
using Veriado.Domain.Files;
using Veriado.Domain.Metadata;

namespace Veriado.Mapping.Profiles;

/// <summary>
/// Configures mappings used when projecting domain entities to read DTOs.
/// </summary>
public sealed class FileReadProfiles : Profile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileReadProfiles"/> class.
    /// </summary>
    public FileReadProfiles()
    {
        CreateMap<FileDocumentValidityEntity, FileValidityDto>().ConstructUsing(src => new FileValidityDto(
            src.IssuedAt.Value,
            src.ValidUntil.Value,
            src.HasPhysicalCopy,
            src.HasElectronicCopy));

        CreateMap<FileSystemMetadata, FileSystemMetadataDto>().ConstructUsing(src => new FileSystemMetadataDto(
            (int)src.Attributes,
            src.CreatedUtc.Value,
            src.LastWriteUtc.Value,
            src.LastAccessUtc.Value,
            src.OwnerSid,
            src.HardLinkCount,
            src.AlternateDataStreamCount));

        CreateMap<FileContentEntity, FileContentDto>().ConstructUsing(src => new FileContentDto(
            src.Hash.Value,
            src.Length.Value,
            null));

        CreateMap<FileEntity, FileSummaryDto>()
            .ForMember(dest => dest.Name, opt => opt.ConvertUsing(new CommonValueConverters.FileNameToStringConverter(), src => src.Name))
            .ForMember(dest => dest.Extension, opt => opt.ConvertUsing(new CommonValueConverters.FileExtensionToStringConverter(), src => src.Extension))
            .ForMember(dest => dest.Mime, opt => opt.ConvertUsing(new CommonValueConverters.MimeTypeToStringConverter(), src => src.Mime))
            .ForMember(dest => dest.Size, opt => opt.ConvertUsing(new CommonValueConverters.ByteSizeToLongConverter(), src => src.Size))
            .ForMember(dest => dest.CreatedUtc, opt => opt.ConvertUsing(new CommonValueConverters.UtcTimestampToDateTimeOffsetConverter(), src => src.CreatedUtc))
            .ForMember(dest => dest.LastModifiedUtc, opt => opt.ConvertUsing(new CommonValueConverters.UtcTimestampToDateTimeOffsetConverter(), src => src.LastModifiedUtc))
            .ForMember(dest => dest.Validity, opt => opt.MapFrom(src => src.Validity))
            .ForMember(dest => dest.IsIndexStale, opt => opt.MapFrom(src => src.SearchIndex.IsStale))
            .ForMember(dest => dest.LastIndexedUtc, opt => opt.MapFrom(src => src.SearchIndex.LastIndexedUtc))
            .ForMember(dest => dest.IndexedTitle, opt => opt.MapFrom(src => src.SearchIndex.IndexedTitle))
            .ForMember(dest => dest.IndexSchemaVersion, opt => opt.MapFrom(src => src.SearchIndex.SchemaVersion))
            .ForMember(dest => dest.IndexedContentHash, opt => opt.MapFrom(src => src.SearchIndex.IndexedContentHash));

        CreateMap<FileEntity, FileDetailDto>()
            .IncludeBase<FileEntity, FileSummaryDto>()
            .ForMember(dest => dest.Content, opt => opt.MapFrom(src => src.Content))
            .ForMember(dest => dest.SystemMetadata, opt => opt.MapFrom(src => src.SystemMetadata))
            .ForMember(dest => dest.ExtendedMetadata, opt =>
            {
                opt.MapFrom(src => src.ExtendedMetadata.AsEnumerable());
                opt.NullSubstitute(Array.Empty<ExtendedMetadataItemDto>());
            })
            .ForMember(dest => dest.Validity, opt => opt.MapFrom(src => src.Validity));
    }
}
