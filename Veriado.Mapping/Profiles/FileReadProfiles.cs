using System;
using AutoMapper;
using Veriado.Appl.Abstractions;
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
        CreateMap<FileDocumentValidityEntity, FileValidityDto>()
            .ConvertUsing(src => new FileValidityDto(
                src.IssuedAt.Value,
                src.ValidUntil.Value,
                src.HasPhysicalCopy,
                src.HasElectronicCopy));

        CreateMap<FileSystemMetadata, FileSystemMetadataDto>()
            .ConvertUsing(src => new FileSystemMetadataDto(
                (int)src.Attributes,
                src.CreatedUtc.Value,
                src.LastWriteUtc.Value,
                src.LastAccessUtc.Value,
                src.OwnerSid,
                src.HardLinkCount,
                src.AlternateDataStreamCount));

        CreateMap<FileContentEntity, FileContentDto>()
            .ConvertUsing(src => new FileContentDto(
                src.Hash.Value,
                src.Length.Value,
                null));

        CreateMap<FileDocumentValidityReadModel, FileValidityDto>()
            .ConvertUsing(src => new FileValidityDto(
                src.IssuedAtUtc,
                src.ValidUntilUtc,
                src.HasPhysicalCopy,
                src.HasElectronicCopy));

        CreateMap<FileListItemReadModel, FileListItemDto>().ConstructUsing(src => new FileListItemDto(
            src.Id,
            src.Name,
            src.Extension,
            src.Mime,
            src.Author,
            src.SizeBytes,
            src.Version,
            src.IsReadOnly,
            src.CreatedUtc,
            src.LastModifiedUtc,
            src.ValidUntilUtc));

        CreateMap<FileEntity, FileSummaryDto>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name.Value))
            .ForMember(dest => dest.Extension, opt => opt.MapFrom(src => src.Extension.Value))
            .ForMember(dest => dest.Mime, opt => opt.MapFrom(src => src.Mime.Value))
            .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Title))
            .ForMember(dest => dest.Size, opt => opt.MapFrom(src => src.Size.Value))
            .ForMember(dest => dest.CreatedUtc, opt => opt.MapFrom(src => src.CreatedUtc.Value))
            .ForMember(dest => dest.LastModifiedUtc, opt => opt.MapFrom(src => src.LastModifiedUtc.Value))
            .ForMember(dest => dest.Validity, opt => opt.MapFrom(src => src.Validity))
            .ForMember(dest => dest.IsIndexStale, opt => opt.MapFrom(src => src.SearchIndex.IsStale))
            .ForMember(dest => dest.LastIndexedUtc, opt => opt.MapFrom(src => src.SearchIndex.LastIndexedUtc))
            .ForMember(dest => dest.IndexedTitle, opt => opt.MapFrom(src => src.SearchIndex.IndexedTitle))
            .ForMember(dest => dest.IndexSchemaVersion, opt => opt.MapFrom(src => src.SearchIndex.SchemaVersion))
            .ForMember(dest => dest.IndexedContentHash, opt => opt.MapFrom(src => src.SearchIndex.IndexedContentHash))
            .ForMember(dest => dest.Score, opt => opt.Ignore());

        CreateMap<FileEntity, FileDetailDto>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name.Value))
            .ForMember(dest => dest.Extension, opt => opt.MapFrom(src => src.Extension.Value))
            .ForMember(dest => dest.Mime, opt => opt.MapFrom(src => src.Mime.Value))
            .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Title))
            .ForMember(dest => dest.Size, opt => opt.MapFrom(src => src.Size.Value))
            .ForMember(dest => dest.CreatedUtc, opt => opt.MapFrom(src => src.CreatedUtc.Value))
            .ForMember(dest => dest.LastModifiedUtc, opt => opt.MapFrom(src => src.LastModifiedUtc.Value))
            .ForMember(dest => dest.Content, opt => opt.MapFrom(src => src.Content))
            .ForMember(dest => dest.SystemMetadata, opt => opt.MapFrom(src => src.SystemMetadata))
            .ForMember(dest => dest.Validity, opt => opt.MapFrom(src => src.Validity))
            .ForMember(dest => dest.IsIndexStale, opt => opt.MapFrom(src => src.SearchIndex.IsStale))
            .ForMember(dest => dest.LastIndexedUtc, opt => opt.MapFrom(src => src.SearchIndex.LastIndexedUtc))
            .ForMember(dest => dest.IndexedTitle, opt => opt.MapFrom(src => src.SearchIndex.IndexedTitle))
            .ForMember(dest => dest.IndexSchemaVersion, opt => opt.MapFrom(src => src.SearchIndex.SchemaVersion))
            .ForMember(dest => dest.IndexedContentHash, opt => opt.MapFrom(src => src.SearchIndex.IndexedContentHash));

        CreateMap<FileDetailReadModel, FileDetailDto>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
            .ForMember(dest => dest.Extension, opt => opt.MapFrom(src => src.Extension))
            .ForMember(dest => dest.Mime, opt => opt.MapFrom(src => src.Mime))
            .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Author))
            .ForMember(dest => dest.Size, opt => opt.MapFrom(src => src.SizeBytes))
            .ForMember(dest => dest.CreatedUtc, opt => opt.MapFrom(src => src.CreatedUtc))
            .ForMember(dest => dest.LastModifiedUtc, opt => opt.MapFrom(src => src.LastModifiedUtc))
            .ForMember(dest => dest.IsReadOnly, opt => opt.MapFrom(src => src.IsReadOnly))
            .ForMember(dest => dest.Version, opt => opt.MapFrom(src => src.Version))
            .ForMember(dest => dest.Content, opt => opt.MapFrom(src => new FileContentDto(string.Empty, src.SizeBytes, null)))
            .ForMember(dest => dest.SystemMetadata, opt => opt.MapFrom(src => src.SystemMetadata))
            .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Title))
            .ForMember(dest => dest.Validity, opt => opt.MapFrom(src => src.Validity))
            .ForMember(dest => dest.IsIndexStale, opt => opt.MapFrom(_ => false))
            .ForMember(dest => dest.LastIndexedUtc, opt => opt.MapFrom(_ => (DateTimeOffset?)null))
            .ForMember(dest => dest.IndexedTitle, opt => opt.MapFrom(_ => (string?)null))
            .ForMember(dest => dest.IndexSchemaVersion, opt => opt.MapFrom(_ => 0))
            .ForMember(dest => dest.IndexedContentHash, opt => opt.MapFrom(_ => (string?)null));
    }
}
