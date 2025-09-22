using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Veriado.Application.Abstractions;
using Veriado.Contracts.Files;
using Veriado.Contracts.Search;
using Veriado.Domain.Files;
using Veriado.Domain.Metadata;
using Veriado.Domain.Search;

namespace Veriado.Application.Mapping;

/// <summary>
/// Provides mapping helpers between domain entities/read models and DTOs.
/// </summary>
public static class DomainToDto
{
    private static readonly FieldInfo MetadataValueField = typeof(MetadataValue)
        .GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// Maps a <see cref="FileEntity"/> aggregate to a <see cref="FileSummaryDto"/>.
    /// </summary>
    /// <param name="file">The domain aggregate.</param>
    /// <returns>The mapped DTO.</returns>
    public static FileSummaryDto ToFileSummaryDto(FileEntity file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new FileSummaryDto
        {
            Id = file.Id,
            Name = file.Name.Value,
            Extension = file.Extension.Value,
            Mime = file.Mime.Value,
            Author = file.Author,
            Size = file.Size.Value,
            CreatedUtc = file.CreatedUtc.Value,
            LastModifiedUtc = file.LastModifiedUtc.Value,
            IsReadOnly = file.IsReadOnly,
            Version = file.Version,
            Validity = ToValidityDto(file.Validity),
            IsIndexStale = file.SearchIndex.IsStale,
            LastIndexedUtc = file.SearchIndex.LastIndexedUtc,
            IndexedTitle = file.SearchIndex.IndexedTitle,
            IndexSchemaVersion = file.SearchIndex.SchemaVersion,
            IndexedContentHash = file.SearchIndex.IndexedContentHash,
        };
    }

    /// <summary>
    /// Maps a read model to a <see cref="FileSummaryDto"/>.
    /// </summary>
    public static FileSummaryDto ToFileSummaryDto(FileDetailReadModel detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return new FileSummaryDto
        {
            Id = detail.Id,
            Name = detail.Name,
            Extension = detail.Extension,
            Mime = detail.Mime,
            Author = detail.Author,
            Size = detail.SizeBytes,
            CreatedUtc = detail.CreatedUtc,
            LastModifiedUtc = detail.LastModifiedUtc,
            IsReadOnly = detail.IsReadOnly,
            Version = detail.Version,
            Validity = ToValidityDto(detail.Validity),
        };
    }

    /// <summary>
    /// Maps a read model to a <see cref="FileListItemDto"/>.
    /// </summary>
    public static FileListItemDto ToFileListItemDto(FileListItemReadModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new FileListItemDto(
            model.Id,
            model.Name,
            model.Extension,
            model.Mime,
            model.Author,
            model.SizeBytes,
            model.Version,
            model.IsReadOnly,
            model.CreatedUtc,
            model.LastModifiedUtc,
            model.ValidUntilUtc);
    }

    /// <summary>
    /// Maps a <see cref="FileEntity"/> to a <see cref="FileDetailDto"/>.
    /// </summary>
    public static FileDetailDto ToDetailDto(FileEntity file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var metadata = ConvertMetadataDictionary(file.ExtendedMetadata);
        return new FileDetailDto
        {
            Id = file.Id,
            Name = file.Name.Value,
            Extension = file.Extension.Value,
            Mime = file.Mime.Value,
            Author = file.Author,
            Size = file.Size.Value,
            CreatedUtc = file.CreatedUtc.Value,
            LastModifiedUtc = file.LastModifiedUtc.Value,
            IsReadOnly = file.IsReadOnly,
            Version = file.Version,
            Content = new FileContentDto(file.Content.Hash.Value, file.Content.Length.Value),
            SystemMetadata = ToMetadataDto(file.SystemMetadata),
            ExtendedMetadata = MapExtendedMetadata(metadata),
            Validity = ToValidityDto(file.Validity),
            IsIndexStale = file.SearchIndex.IsStale,
            LastIndexedUtc = file.SearchIndex.LastIndexedUtc,
            IndexedTitle = file.SearchIndex.IndexedTitle,
            IndexSchemaVersion = file.SearchIndex.SchemaVersion,
            IndexedContentHash = file.SearchIndex.IndexedContentHash,
        };
    }

    /// <summary>
    /// Maps a <see cref="FileDetailReadModel"/> to a <see cref="FileDetailDto"/>.
    /// </summary>
    public static FileDetailDto ToDetailDto(FileDetailReadModel detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var metadata = detail.ExtendedMetadata is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(detail.ExtendedMetadata, StringComparer.Ordinal);
        return new FileDetailDto
        {
            Id = detail.Id,
            Name = detail.Name,
            Extension = detail.Extension,
            Mime = detail.Mime,
            Author = detail.Author,
            Size = detail.SizeBytes,
            CreatedUtc = detail.CreatedUtc,
            LastModifiedUtc = detail.LastModifiedUtc,
            IsReadOnly = detail.IsReadOnly,
            Version = detail.Version,
            Content = new FileContentDto(string.Empty, detail.SizeBytes),
            SystemMetadata = ToMetadataDto(detail.SystemMetadata),
            ExtendedMetadata = MapExtendedMetadata(metadata),
            Validity = ToValidityDto(detail.Validity),
        };
    }

    /// <summary>
    /// Maps a search hit projection to its DTO.
    /// </summary>
    public static SearchHitDto ToSearchHitDto(SearchHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        return new SearchHitDto(hit.FileId, hit.Title, hit.Mime, hit.Snippet, hit.Score, hit.LastModifiedUtc);
    }

    private static FileSystemMetadataDto ToMetadataDto(FileSystemMetadata metadata)
    {
        return new FileSystemMetadataDto(
            (int)metadata.Attributes,
            metadata.CreatedUtc.Value,
            metadata.LastWriteUtc.Value,
            metadata.LastAccessUtc.Value,
            metadata.OwnerSid,
            metadata.HardLinkCount,
            metadata.AlternateDataStreamCount);
    }

    private static FileValidityDto? ToValidityDto(FileDocumentValidityEntity? validity)
    {
        if (validity is null)
        {
            return null;
        }

        return new FileValidityDto(
            validity.IssuedAt.Value,
            validity.ValidUntil.Value,
            validity.HasPhysicalCopy,
            validity.HasElectronicCopy);
    }

    private static FileValidityDto? ToValidityDto(FileDocumentValidityReadModel? validity)
    {
        if (validity is null)
        {
            return null;
        }

        return new FileValidityDto(
            validity.IssuedAtUtc,
            validity.ValidUntilUtc,
            validity.HasPhysicalCopy,
            validity.HasElectronicCopy);
    }

    private static IReadOnlyDictionary<string, string?> ConvertMetadataDictionary(ExtendedMetadata metadata)
    {
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in metadata.AsEnumerable())
        {
            var key = pair.Key.ToString();
            dictionary[key] = FormatMetadataValue(pair.Value);
        }

        return dictionary;
    }

    private static IReadOnlyList<ExtendedMetadataItemDto> MapExtendedMetadata(IReadOnlyDictionary<string, string?> metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return Array.Empty<ExtendedMetadataItemDto>();
        }

        var items = new List<ExtendedMetadataItemDto>(metadata.Count);
        foreach (var pair in metadata)
        {
            if (!TryParsePropertyKey(pair.Key, out var formatId, out var propertyId))
            {
                continue;
            }

            items.Add(new ExtendedMetadataItemDto
            {
                FormatId = formatId,
                PropertyId = propertyId,
                Value = pair.Value is null
                    ? new MetadataValueDto { Kind = MetadataValueDtoKind.Null }
                    : new MetadataValueDto
                    {
                        Kind = MetadataValueDtoKind.String,
                        StringValue = pair.Value,
                    },
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

        var parts = key.Split('/', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!Guid.TryParse(parts[0], out formatId))
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out propertyId))
        {
            formatId = Guid.Empty;
            return false;
        }

        return true;
    }

    private static string? FormatMetadataValue(MetadataValue value)
    {
        if (value.TryGetString(out var single))
        {
            return single;
        }

        if (value.TryGetStringArray(out var array) && array is not null)
        {
            return string.Join(", ", array);
        }

        if (value.TryGetGuid(out var guid))
        {
            return guid.ToString("D", CultureInfo.InvariantCulture);
        }

        if (value.TryGetFileTime(out var fileTime))
        {
            return fileTime.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value.TryGetBinary(out var binary) && binary is not null)
        {
            return Convert.ToBase64String(binary);
        }

        var raw = MetadataValueField.GetValue(value);
        return raw switch
        {
            null => null,
            bool boolean => boolean.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            uint unsigned => unsigned.ToString(CultureInfo.InvariantCulture),
            double dbl => dbl.ToString(CultureInfo.InvariantCulture),
            _ => raw.ToString(),
        };
    }
}
