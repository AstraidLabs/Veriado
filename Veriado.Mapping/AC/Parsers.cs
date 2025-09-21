using System;
using System.Collections.Generic;
using System.Linq;
using Veriado.Application.UseCases.Files.SetExtendedMetadata;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Mapping.AC;

/// <summary>
/// Provides anti-corruption parsing helpers converting external DTOs into domain value objects.
/// </summary>
internal static class Parsers
{
    internal static ParserResult<FileName> ParseFileName(string? value, string target)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ParserResult<FileName>.Failure(ApiError.MissingValue(target));
        }

        try
        {
            return ParserResult<FileName>.Success(FileName.From(value));
        }
        catch (Exception ex)
        {
            return ParserResult<FileName>.Failure(ApiError.ForValue(target, ex.Message));
        }
    }

    internal static ParserResult<FileExtension> ParseFileExtension(string? value, string target)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ParserResult<FileExtension>.Failure(ApiError.MissingValue(target));
        }

        try
        {
            return ParserResult<FileExtension>.Success(FileExtension.From(value));
        }
        catch (Exception ex)
        {
            return ParserResult<FileExtension>.Failure(ApiError.ForValue(target, ex.Message));
        }
    }

    internal static ParserResult<MimeType> ParseMimeType(string? value, string target)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ParserResult<MimeType>.Failure(ApiError.MissingValue(target));
        }

        try
        {
            return ParserResult<MimeType>.Success(MimeType.From(value));
        }
        catch (Exception ex)
        {
            return ParserResult<MimeType>.Failure(ApiError.ForValue(target, ex.Message));
        }
    }

    internal static ParserResult<FileSystemMetadata> ParseFileSystemMetadata(FileSystemMetadataDto dto, string target)
    {
        try
        {
            var metadata = new FileSystemMetadata(
                (FileAttributesFlags)dto.Attributes,
                UtcTimestamp.From(dto.CreatedUtc),
                UtcTimestamp.From(dto.LastWriteUtc),
                UtcTimestamp.From(dto.LastAccessUtc),
                dto.OwnerSid,
                dto.HardLinkCount,
                dto.AlternateDataStreamCount);
            return ParserResult<FileSystemMetadata>.Success(metadata);
        }
        catch (Exception ex)
        {
            return ParserResult<FileSystemMetadata>.Failure(ApiError.ForValue(target, ex.Message));
        }
    }

    internal static IReadOnlyCollection<ExtendedMetadataEntry> ParseMetadataPatches(
        IReadOnlyList<ExtendedMetadataItemDto>? items,
        string target,
        ICollection<ApiError> errors)
    {
        if (items is null || items.Count == 0)
        {
            return Array.Empty<ExtendedMetadataEntry>();
        }

        var entries = new List<ExtendedMetadataEntry>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var result = ParseMetadataEntry(items[i], $"{target}[{i}]");
            if (!result.IsSuccess)
            {
                errors.Add(result.Error!);
                continue;
            }

            entries.Add(result.Value);
        }

        return entries;
    }

    internal static ParserResult<ExtendedMetadataEntry> ParseMetadataEntry(ExtendedMetadataItemDto dto, string target)
    {
        if (dto is null)
        {
            return ParserResult<ExtendedMetadataEntry>.Failure(ApiError.MissingValue(target));
        }

        if (dto.FormatId == Guid.Empty)
        {
            return ParserResult<ExtendedMetadataEntry>.Failure(ApiError.ForValue($"{target}.FormatId", "Format identifier must be a non-empty GUID."));
        }

        if (dto.Remove)
        {
            return ParserResult<ExtendedMetadataEntry>.Success(new ExtendedMetadataEntry(dto.FormatId, dto.PropertyId, null));
        }

        if (dto.Value is null)
        {
            return ParserResult<ExtendedMetadataEntry>.Failure(ApiError.MissingValue($"{target}.Value"));
        }

        var valueResult = ParseMetadataValue(dto.Value, $"{target}.Value");
        if (!valueResult.IsSuccess)
        {
            return ParserResult<ExtendedMetadataEntry>.Failure(valueResult.Error!);
        }

        return ParserResult<ExtendedMetadataEntry>.Success(new ExtendedMetadataEntry(dto.FormatId, dto.PropertyId, valueResult.Value));
    }

    internal static ParserResult<MetadataValue> ParseMetadataValue(MetadataValueDto dto, string target)
    {
        try
        {
            var value = dto.Kind switch
            {
                MetadataValueDtoKind.Null => MetadataValue.Null,
                MetadataValueDtoKind.String => MetadataValue.FromString(dto.StringValue ?? throw new ArgumentException("String value is required.")),
                MetadataValueDtoKind.StringArray => MetadataValue.FromStringArray(dto.StringArrayValue ?? throw new ArgumentException("String array value is required.")),
                MetadataValueDtoKind.UInt32 => MetadataValue.FromUInt(dto.UInt32Value ?? throw new ArgumentException("UInt32 value is required.")),
                MetadataValueDtoKind.Int32 => MetadataValue.FromInt(dto.Int32Value ?? throw new ArgumentException("Int32 value is required.")),
                MetadataValueDtoKind.Double => MetadataValue.FromReal(dto.DoubleValue ?? throw new ArgumentException("Double value is required.")),
                MetadataValueDtoKind.Boolean => MetadataValue.FromBool(dto.BooleanValue ?? throw new ArgumentException("Boolean value is required.")),
                MetadataValueDtoKind.Guid => MetadataValue.FromGuid(dto.GuidValue ?? throw new ArgumentException("Guid value is required.")),
                MetadataValueDtoKind.FileTime => MetadataValue.FromFileTime(dto.FileTimeValue ?? throw new ArgumentException("Timestamp value is required.")),
                MetadataValueDtoKind.Binary => MetadataValue.FromBinary(dto.BinaryValue ?? throw new ArgumentException("Binary value is required.")),
                _ => throw new NotSupportedException($"Unsupported metadata value kind '{dto.Kind}'."),
            };

            return ParserResult<MetadataValue>.Success(value);
        }
        catch (Exception ex)
        {
            return ParserResult<MetadataValue>.Failure(ApiError.ForValue(target, ex.Message));
        }
    }

    internal static FileSystemMetadata? ParseOptionalMetadata(
        FileSystemMetadataDto? dto,
        string target,
        ICollection<ApiError> errors)
    {
        if (dto is null)
        {
            return null;
        }

        var result = ParseFileSystemMetadata(dto, target);
        if (!result.IsSuccess)
        {
            errors.Add(result.Error!);
            return null;
        }

        return result.Value;
    }

    internal static ParserResult<UtcTimestamp> ParseUtcTimestamp(DateTimeOffset value)
    {
        try
        {
            return ParserResult<UtcTimestamp>.Success(UtcTimestamp.From(value));
        }
        catch (Exception ex)
        {
            return ParserResult<UtcTimestamp>.Failure(ApiError.ForValue(nameof(value), ex.Message));
        }
    }
}

internal readonly struct ParserResult<T>
{
    private ParserResult(bool success, T value, ApiError? error)
    {
        IsSuccess = success;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public T Value { get; }

    public ApiError? Error { get; }

    public static ParserResult<T> Success(T value) => new(true, value, default);

    public static ParserResult<T> Failure(ApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ParserResult<T>(false, default!, error);
    }
}
