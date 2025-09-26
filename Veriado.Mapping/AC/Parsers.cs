using System;
using System.Collections.Generic;
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
