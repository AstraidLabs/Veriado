using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AutoMapper;
using Veriado.Application.Abstractions;
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

        CreateMap<FileDocumentValidityReadModel, FileValidityDto>().ConstructUsing(src => new FileValidityDto(
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
            .ForMember(dest => dest.ExtendedMetadata, opt =>
            {
                opt.MapFrom(src => src.ExtendedMetadata ?? new Dictionary<string, string?>(StringComparer.Ordinal));
                opt.NullSubstitute(Array.Empty<ExtendedMetadataItemDto>());
            })
            .ForMember(dest => dest.Validity, opt => opt.MapFrom(src => src.Validity))
            .ForMember(dest => dest.IsIndexStale, opt => opt.MapFrom(_ => false))
            .ForMember(dest => dest.LastIndexedUtc, opt => opt.MapFrom(_ => (DateTimeOffset?)null))
            .ForMember(dest => dest.IndexedTitle, opt => opt.MapFrom(_ => (string?)null))
            .ForMember(dest => dest.IndexSchemaVersion, opt => opt.MapFrom(_ => 0))
            .ForMember(dest => dest.IndexedContentHash, opt => opt.MapFrom(_ => (string?)null));

        CreateMap<IReadOnlyDictionary<string, string?>, IReadOnlyList<ExtendedMetadataItemDto>>()
            .ConvertUsing(ConvertExtendedMetadataDictionary);
    }

    private static IReadOnlyList<ExtendedMetadataItemDto> ConvertExtendedMetadataDictionary(
        IReadOnlyDictionary<string, string?> source,
        IReadOnlyList<ExtendedMetadataItemDto> destination,
        ResolutionContext context)
    {
        if (source is null || source.Count == 0)
        {
            return Array.Empty<ExtendedMetadataItemDto>();
        }

        var items = new List<ExtendedMetadataItemDto>(source.Count);
        foreach (var pair in source)
        {
            if (!TryParsePropertyKey(pair.Key, out var formatId, out var propertyId))
            {
                continue;
            }

            var value = pair.Value is null
                ? new MetadataValueDto { Kind = MetadataValueDtoKind.Null }
                : new MetadataValueDto
                {
                    Kind = MetadataValueDtoKind.String,
                    StringValue = pair.Value,
                };

            items.Add(new ExtendedMetadataItemDto
            {
                FormatId = formatId,
                PropertyId = propertyId,
                Value = value,
                Remove = false,
            });
        }

        return items.Count == 0 ? Array.Empty<ExtendedMetadataItemDto>() : items;
    }

    private static bool TryParsePropertyKey(string key, out Guid formatId, out int propertyId)
    {
        formatId = Guid.Empty;
        propertyId = 0;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!Guid.TryParse(parts[0], out formatId))
        {
            formatId = Guid.Empty;
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out propertyId))
        {
            formatId = Guid.Empty;
            propertyId = 0;
            return false;
        }

        return true;
    }
}
