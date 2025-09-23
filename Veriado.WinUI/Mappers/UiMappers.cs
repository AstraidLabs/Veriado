using System;
using System.Collections.Generic;
using System.Globalization;
using Veriado.Contracts.Files;
using Veriado.Contracts.Search;
using Veriado.Models.Files;
using Veriado.Models.Search;

namespace Veriado.Mappers;

/// <summary>
/// Provides helper extensions to convert contracts DTOs into UI-facing models.
/// </summary>
public static class UiMappers
{
    public static FileListItemModel ToFileListItemModel(this FileSummaryDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new FileListItemModel
        {
            Id = dto.Id,
            Name = dto.Name,
            Extension = dto.Extension,
            Mime = dto.Mime,
            Author = dto.Author,
            SizeBytes = dto.Size,
            CreatedUtc = dto.CreatedUtc,
            LastModifiedUtc = dto.LastModifiedUtc,
            IsReadOnly = dto.IsReadOnly,
            ValidUntilUtc = dto.Validity?.ValidUntil,
            IsIndexStale = dto.IsIndexStale,
            Score = dto.Score,
        };
    }

    public static FileDetailModel ToFileDetailModel(this FileDetailDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new FileDetailModel
        {
            Id = dto.Id,
            Name = dto.Name,
            Extension = dto.Extension,
            Mime = dto.Mime,
            Author = dto.Author,
            SizeBytes = dto.Size,
            CreatedUtc = dto.CreatedUtc,
            LastModifiedUtc = dto.LastModifiedUtc,
            IsReadOnly = dto.IsReadOnly,
            Version = dto.Version,
            Content = dto.Content,
            SystemMetadata = dto.SystemMetadata,
            ExtendedMetadata = MapExtendedMetadata(dto.ExtendedMetadata),
            Validity = dto.Validity,
            IsIndexStale = dto.IsIndexStale,
            LastIndexedUtc = dto.LastIndexedUtc,
            IndexedTitle = dto.IndexedTitle,
            IndexSchemaVersion = dto.IndexSchemaVersion,
            IndexedContentHash = dto.IndexedContentHash,
        };
    }

    public static SearchHistoryItemModel ToSearchHistoryItemModel(this SearchHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new SearchHistoryItemModel
        {
            Id = entry.Id,
            QueryText = entry.QueryText,
            MatchQuery = entry.MatchQuery,
            LastQueriedUtc = entry.LastQueriedUtc,
            Executions = entry.Executions,
            LastTotalHits = entry.LastTotalHits,
            IsFuzzy = entry.IsFuzzy,
        };
    }

    public static SearchFavoriteItemModel ToSearchFavoriteItemModel(this SearchFavoriteItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new SearchFavoriteItemModel
        {
            Id = item.Id,
            Name = item.Name,
            QueryText = item.QueryText,
            MatchQuery = item.MatchQuery,
            Position = item.Position,
            CreatedUtc = item.CreatedUtc,
            IsFuzzy = item.IsFuzzy,
        };
    }

    public static SearchFavoriteDefinitionModel ToSearchFavoriteDefinitionModel(this SearchFavoriteDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new SearchFavoriteDefinitionModel
        {
            Name = definition.Name,
            MatchQuery = definition.MatchQuery,
            QueryText = definition.QueryText,
            IsFuzzy = definition.IsFuzzy,
        };
    }

    public static SearchHitModel ToSearchHitModel(this SearchHitDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new SearchHitModel
        {
            FileId = dto.FileId,
            Title = dto.Title,
            Mime = dto.Mime,
            Snippet = dto.Snippet,
            Score = dto.Score,
            LastModifiedUtc = dto.LastModifiedUtc,
        };
    }

    private static IReadOnlyDictionary<string, string?> MapExtendedMetadata(IReadOnlyList<ExtendedMetadataItemDto> metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string?>(metadata.Count, StringComparer.Ordinal);
        foreach (var item in metadata)
        {
            if (item is null || item.Remove)
            {
                continue;
            }

            var key = $"{item.FormatId:D}:{item.PropertyId}";
            result[key] = FormatMetadataValue(item.Value);
        }

        return result;
    }

    private static string? FormatMetadataValue(MetadataValueDto? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Kind switch
        {
            MetadataValueDtoKind.Null => null,
            MetadataValueDtoKind.String => value.StringValue,
            MetadataValueDtoKind.StringArray => value.StringArrayValue is null
                ? null
                : string.Join(", ", value.StringArrayValue),
            MetadataValueDtoKind.UInt32 => value.UInt32Value?.ToString(CultureInfo.InvariantCulture),
            MetadataValueDtoKind.Int32 => value.Int32Value?.ToString(CultureInfo.InvariantCulture),
            MetadataValueDtoKind.Double => value.DoubleValue?.ToString(CultureInfo.InvariantCulture),
            MetadataValueDtoKind.Boolean => value.BooleanValue?.ToString(CultureInfo.InvariantCulture),
            MetadataValueDtoKind.Guid => value.GuidValue?.ToString(),
            MetadataValueDtoKind.FileTime => value.FileTimeValue?.ToString("u", CultureInfo.InvariantCulture),
            MetadataValueDtoKind.Binary => value.BinaryValue is null
                ? null
                : Convert.ToHexString(value.BinaryValue),
            _ => value.ToString(),
        };
    }
}
