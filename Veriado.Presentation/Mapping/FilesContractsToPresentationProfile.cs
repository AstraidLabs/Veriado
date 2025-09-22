using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AutoMapper;
using CommunityToolkit.Mvvm.Collections;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Presentation.Models.Common;
using Veriado.Presentation.Models.Files;

namespace Veriado.Presentation.Mapping;

public sealed class FilesContractsToPresentationProfile : Profile
{
    public FilesContractsToPresentationProfile()
    {
        CreateMap<FileValidityDto, FileValidityModel>().ReverseMap();
        CreateMap<FileSystemMetadataDto, FileSystemMetadataModel>().ReverseMap();
        CreateMap<FileContentDto, FileContentModel>().ReverseMap();
        CreateMap<FileSortSpecDto, FileSortSpecModel>().ReverseMap();
        CreateMap<PageRequest, PageRequestModel>().ReverseMap();

        CreateMap<FileSummaryDto, FileSummaryModel>().ReverseMap();

        CreateMap<IReadOnlyList<ExtendedMetadataItemDto>, ObservableDictionary<string, string?>>()
            .ConvertUsing(ToDictionary);
        CreateMap<ObservableDictionary<string, string?>, IReadOnlyList<ExtendedMetadataItemDto>>()
            .ConvertUsing(ToExtendedMetadata);

        CreateMap<FileDetailDto, FileDetailModel>();
        CreateMap<FileDetailModel, FileDetailDto>();

        CreateMap<ICollection<FileSortSpecDto>, ObservableCollection<FileSortSpecModel>>()
            .ConvertUsing((source, _, context) =>
            {
                var collection = new ObservableCollection<FileSortSpecModel>();
                if (source is null)
                {
                    return collection;
                }

                foreach (var item in source)
                {
                    collection.Add(context.Mapper.Map<FileSortSpecModel>(item));
                }

                return collection;
            });

        CreateMap<ObservableCollection<FileSortSpecModel>, List<FileSortSpecDto>>()
            .ConvertUsing((source, _, context) =>
            {
                var list = new List<FileSortSpecDto>(source?.Count ?? 0);
                if (source is null)
                {
                    return list;
                }

                foreach (var item in source)
                {
                    list.Add(context.Mapper.Map<FileSortSpecDto>(item));
                }

                return list;
            });

        CreateMap<FileGridQueryDto, FileGridQueryModel>();
        CreateMap<FileGridQueryModel, FileGridQueryDto>();

        CreateMap(typeof(PageResult<>), typeof(PageResultModel<>))
            .ConvertUsing(typeof(PageResultToModelConverter<,>));
        CreateMap(typeof(PageResultModel<>), typeof(PageResult<>))
            .ConvertUsing(typeof(PageResultModelToDtoConverter<,>));
    }

    private static ObservableDictionary<string, string?> ToDictionary(
        IReadOnlyList<ExtendedMetadataItemDto>? source,
        ObservableDictionary<string, string?>? _,
        ResolutionContext __)
    {
        var dictionary = new ObservableDictionary<string, string?>();
        if (source is null)
        {
            return dictionary;
        }

        foreach (var item in source)
        {
            var key = CreateMetadataKey(item.FormatId, item.PropertyId);
            dictionary[key] = FormatMetadataValue(item.Value);
        }

        return dictionary;
    }

    private static IReadOnlyList<ExtendedMetadataItemDto> ToExtendedMetadata(
        ObservableDictionary<string, string?>? source,
        IReadOnlyList<ExtendedMetadataItemDto>? _,
        ResolutionContext __)
    {
        if (source is null || source.Count == 0)
        {
            return Array.Empty<ExtendedMetadataItemDto>();
        }

        var items = new List<ExtendedMetadataItemDto>(source.Count);
        foreach (var pair in source)
        {
            if (!TryParseMetadataKey(pair.Key, out var formatId, out var propertyId))
            {
                continue;
            }

            var value = pair.Value;
            var metadata = value is null
                ? null
                : new MetadataValueDto
                {
                    Kind = MetadataValueDtoKind.String,
                    StringValue = value,
                };

            items.Add(new ExtendedMetadataItemDto
            {
                FormatId = formatId,
                PropertyId = propertyId,
                Value = metadata,
                Remove = value is null,
            });
        }

        return items;
    }

    private static string CreateMetadataKey(Guid formatId, int propertyId)
        => $"{formatId:D}:{propertyId}";

    private static bool TryParseMetadataKey(string key, out Guid formatId, out int propertyId)
    {
        formatId = Guid.Empty;
        propertyId = 0;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!Guid.TryParse(parts[0], out formatId))
        {
            formatId = Guid.Empty;
            return false;
        }

        if (!int.TryParse(parts[1], out propertyId))
        {
            propertyId = 0;
            return false;
        }

        return true;
    }

    private static string? FormatMetadataValue(MetadataValueDto? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Kind switch
        {
            MetadataValueDtoKind.String => value.StringValue,
            MetadataValueDtoKind.StringArray => value.StringArrayValue is null
                ? null
                : string.Join(", ", value.StringArrayValue),
            MetadataValueDtoKind.UInt32 => value.UInt32Value?.ToString(),
            MetadataValueDtoKind.Int32 => value.Int32Value?.ToString(),
            MetadataValueDtoKind.Double => value.DoubleValue?.ToString(),
            MetadataValueDtoKind.Boolean => value.BooleanValue?.ToString(),
            MetadataValueDtoKind.Guid => value.GuidValue?.ToString(),
            MetadataValueDtoKind.FileTime => value.FileTimeValue?.ToString("O"),
            MetadataValueDtoKind.Binary => value.BinaryValue is null
                ? null
                : $"{value.BinaryValue.Length} bytes",
            _ => null,
        };
    }
}
