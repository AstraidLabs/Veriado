using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Veriado.Application.Abstractions;
using Veriado.Application.DTO;
using Veriado.Domain.Files;
using Veriado.Domain.Metadata;

namespace Veriado.Application.Mapping;

/// <summary>
/// Provides mapping helpers between domain entities/read models and DTOs.
/// </summary>
public static class DomainToDto
{
    private static readonly FieldInfo MetadataValueField = typeof(MetadataValue)
        .GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// Maps a <see cref="FileEntity"/> aggregate to a <see cref="FileDto"/>.
    /// </summary>
    /// <param name="file">The domain aggregate.</param>
    /// <returns>The mapped DTO.</returns>
    public static FileDto ToFileDto(FileEntity file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new FileDto(
            file.Id,
            file.Name.Value,
            file.Extension.Value,
            file.Mime.Value,
            file.Author,
            file.Size.Value,
            file.Version,
            file.IsReadOnly,
            file.CreatedUtc.Value,
            file.LastModifiedUtc.Value,
            ToValidityDto(file.Validity));
    }

    /// <summary>
    /// Maps a read model to a <see cref="FileDto"/>.
    /// </summary>
    public static FileDto ToFileDto(FileDetailReadModel detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return new FileDto(
            detail.Id,
            detail.Name,
            detail.Extension,
            detail.Mime,
            detail.Author,
            detail.SizeBytes,
            detail.Version,
            detail.IsReadOnly,
            detail.CreatedUtc,
            detail.LastModifiedUtc,
            ToValidityDto(detail.Validity));
    }

    /// <summary>
    /// Maps a read model to a <see cref="FileListItemDto"/>.
    /// </summary>
    public static FileListItemDto ToListItemDto(FileListItemReadModel model)
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
        return new FileDetailDto(
            ToFileDto(file),
            ToMetadataDto(file.SystemMetadata),
            metadata,
            file.GetTitle(),
            file.GetSubject(),
            file.GetCompany(),
            file.GetManager(),
            file.GetComments());
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
        var title = GetMetadataValue(metadata, WindowsPropertyIds.Title);
        var subject = GetMetadataValue(metadata, WindowsPropertyIds.Subject);
        var company = GetMetadataValue(metadata, WindowsPropertyIds.Company);
        var manager = GetMetadataValue(metadata, WindowsPropertyIds.Manager);
        var comments = GetMetadataValue(metadata, WindowsPropertyIds.Comments);
        return new FileDetailDto(
            ToFileDto(detail),
            ToMetadataDto(detail.SystemMetadata),
            metadata,
            title,
            subject,
            company,
            manager,
            comments);
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
            metadata.Attributes,
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

    private static string? GetMetadataValue(IReadOnlyDictionary<string, string?> metadata, PropertyKey key)
    {
        return metadata.TryGetValue(key.ToString(), out var value) ? value : null;
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
