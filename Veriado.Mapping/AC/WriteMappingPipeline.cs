using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;
using Veriado.Appl.UseCases.Files.RenameFile;
using Veriado.Appl.Common;
using Veriado.Appl.UseCases.Files.ApplySystemMetadata;
using Veriado.Appl.UseCases.Files.SetFileValidity;
using Veriado.Appl.UseCases.Files.UpdateFileMetadata;
using Veriado.Appl.UseCases.Files.CreateFile;
using Veriado.Appl.UseCases.Files.ReplaceFileContent;
using Veriado.Appl.UseCases.Files.SetFileReadOnly;
using Veriado.Appl.UseCases.Files.ClearFileValidity;

namespace Veriado.Mapping.AC;

/// <summary>
/// Coordinates DTO-to-command mapping, value object parsing and validation for write operations.
/// </summary>
public sealed class WriteMappingPipeline
{
    private readonly IValidator<CreateFileCommand> _createValidator;
    private readonly IValidator<ReplaceFileContentCommand> _replaceValidator;
    private readonly IValidator<UpdateFileMetadataCommand> _updateValidator;
    private readonly IValidator<RenameFileCommand> _renameValidator;
    private readonly IValidator<SetFileValidityCommand> _setValidityValidator;
    private readonly IValidator<ClearFileValidityCommand> _clearValidityValidator;

    /// <summary>
    /// Initializes a new instance of the <see cref="WriteMappingPipeline"/> class.
    /// </summary>
    public WriteMappingPipeline(
        IValidator<CreateFileCommand> createValidator,
        IValidator<ReplaceFileContentCommand> replaceValidator,
        IValidator<UpdateFileMetadataCommand> updateValidator,
        IValidator<RenameFileCommand> renameValidator,
        IValidator<SetFileValidityCommand> setValidityValidator,
        IValidator<ClearFileValidityCommand> clearValidityValidator)
    {
        _createValidator = createValidator ?? throw new ArgumentNullException(nameof(createValidator));
        _replaceValidator = replaceValidator ?? throw new ArgumentNullException(nameof(replaceValidator));
        _updateValidator = updateValidator ?? throw new ArgumentNullException(nameof(updateValidator));
        _renameValidator = renameValidator ?? throw new ArgumentNullException(nameof(renameValidator));
        _setValidityValidator = setValidityValidator ?? throw new ArgumentNullException(nameof(setValidityValidator));
        _clearValidityValidator = clearValidityValidator ?? throw new ArgumentNullException(nameof(clearValidityValidator));
    }

    /// <summary>
    /// Maps a create request to a validated command with follow-up instructions.
    /// </summary>
    public async Task<ApiResponse<CreateFileMappedRequest>> MapCreateAsync(CreateFileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<ApiError>();

        var nameResult = Parsers.ParseFileName(request.Name, nameof(request.Name));
        var extensionResult = Parsers.ParseFileExtension(request.Extension, nameof(request.Extension));
        var mimeResult = Parsers.ParseMimeType(request.Mime, nameof(request.Mime));
        var systemMetadata = Parsers.ParseOptionalMetadata(request.SystemMetadata, nameof(request.SystemMetadata), errors);

        CollectError(nameResult, errors);
        CollectError(extensionResult, errors);
        CollectError(mimeResult, errors);

        if (errors.Count > 0)
        {
            return ApiResponse<CreateFileMappedRequest>.Failure(errors);
        }

        var command = new CreateFileCommand(
            nameResult.Value.Value,
            extensionResult.Value.Value,
            mimeResult.Value.Value,
            request.Author ?? string.Empty,
            request.Content ?? Array.Empty<byte>());

        var validation = await _createValidator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            AddValidationErrors(errors, validation);
            return ApiResponse<CreateFileMappedRequest>.Failure(errors);
        }

        var mapped = new CreateFileMappedRequest(command, systemMetadata, request.IsReadOnly);
        return ApiResponse<CreateFileMappedRequest>.Success(mapped);
    }

    /// <summary>
    /// Maps a replace content request to a validated command.
    /// </summary>
    public async Task<ApiResponse<ReplaceFileContentCommand>> MapReplaceContentAsync(ReplaceContentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new ReplaceFileContentCommand(request.FileId, request.Content ?? Array.Empty<byte>());
        var validation = await _replaceValidator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            var errors = new List<ApiError>();
            AddValidationErrors(errors, validation);
            return ApiResponse<ReplaceFileContentCommand>.Failure(errors);
        }

        return ApiResponse<ReplaceFileContentCommand>.Success(command);
    }

    /// <summary>
    /// Maps an update metadata request to a set of commands executed in sequence.
    /// </summary>
    public async Task<ApiResponse<UpdateFileMetadataMappedRequest>> MapUpdateMetadataAsync(UpdateMetadataRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<ApiError>();

        string? mime = null;
        if (!string.IsNullOrWhiteSpace(request.Mime))
        {
            var mimeResult = Parsers.ParseMimeType(request.Mime, nameof(request.Mime));
            if (!mimeResult.IsSuccess)
            {
                CollectError(mimeResult, errors);
            }
            else
            {
                mime = mimeResult.Value.Value;
            }
        }

        var systemMetadata = Parsers.ParseOptionalMetadata(request.SystemMetadata, nameof(request.SystemMetadata), errors);

        if (errors.Count > 0)
        {
            return ApiResponse<UpdateFileMetadataMappedRequest>.Failure(errors);
        }

        UpdateFileMetadataCommand? metadataCommand = null;
        if (mime is not null || request.Author is not null)
        {
            metadataCommand = new UpdateFileMetadataCommand(request.FileId, mime, request.Author);
            var validation = await _updateValidator.ValidateAsync(metadataCommand, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                AddValidationErrors(errors, validation);
            }
        }

        ApplySystemMetadataCommand? systemCommand = systemMetadata is { } metadata
            ? CreateSystemMetadataCommand(request.FileId, metadata)
            : null;

        SetFileReadOnlyCommand? readOnlyCommand = request.IsReadOnly.HasValue
            ? new SetFileReadOnlyCommand(request.FileId, request.IsReadOnly.Value)
            : null;

        if (errors.Count > 0)
        {
            return ApiResponse<UpdateFileMetadataMappedRequest>.Failure(errors);
        }

        if (metadataCommand is null && systemCommand is null && readOnlyCommand is null)
        {
            errors.Add(ApiError.ForValue(nameof(UpdateMetadataRequest), "At least one metadata change must be provided."));
            return ApiResponse<UpdateFileMetadataMappedRequest>.Failure(errors);
        }

        var mapped = new UpdateFileMetadataMappedRequest(metadataCommand, systemCommand, readOnlyCommand);
        return ApiResponse<UpdateFileMetadataMappedRequest>.Success(mapped);
    }

    /// <summary>
    /// Maps a rename request to a validated command.
    /// </summary>
    public async Task<ApiResponse<RenameFileCommand>> MapRenameAsync(Guid fileId, string newName, CancellationToken cancellationToken)
    {
        var errors = new List<ApiError>();
        if (fileId == Guid.Empty)
        {
            errors.Add(ApiError.ForValue(nameof(fileId), "File identifier must be a non-empty GUID."));
        }

        var nameResult = Parsers.ParseFileName(newName, nameof(newName));
        CollectError(nameResult, errors);

        if (errors.Count > 0)
        {
            return ApiResponse<RenameFileCommand>.Failure(errors);
        }

        var command = new RenameFileCommand(fileId, nameResult.Value.Value);
        var validation = await _renameValidator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            AddValidationErrors(errors, validation);
            return ApiResponse<RenameFileCommand>.Failure(errors);
        }

        return ApiResponse<RenameFileCommand>.Success(command);
    }

    /// <summary>
    /// Maps a set-validity request to a validated command.
    /// </summary>
    public async Task<ApiResponse<SetFileValidityCommand>> MapSetValidityAsync(SetValidityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var issuedResult = Parsers.ParseUtcTimestamp(request.IssuedAt);
        var untilResult = Parsers.ParseUtcTimestamp(request.ValidUntil);
        var errors = new List<ApiError>();
        CollectError(issuedResult, errors);
        CollectError(untilResult, errors);

        if (errors.Count > 0)
        {
            return ApiResponse<SetFileValidityCommand>.Failure(errors);
        }

        var command = new SetFileValidityCommand(
            request.FileId,
            issuedResult.Value.Value,
            untilResult.Value.Value,
            request.HasPhysicalCopy,
            request.HasElectronicCopy);

        var validation = await _setValidityValidator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            AddValidationErrors(errors, validation);
            return ApiResponse<SetFileValidityCommand>.Failure(errors);
        }

        return ApiResponse<SetFileValidityCommand>.Success(command);
    }

    /// <summary>
    /// Maps a clear-validity request to a validated command.
    /// </summary>
    public async Task<ApiResponse<ClearFileValidityCommand>> MapClearValidityAsync(ClearValidityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new ClearFileValidityCommand(request.FileId);
        var validation = await _clearValidityValidator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            var errors = new List<ApiError>();
            AddValidationErrors(errors, validation);
            return ApiResponse<ClearFileValidityCommand>.Failure(errors);
        }

        return ApiResponse<ClearFileValidityCommand>.Success(command);
    }

    private static ApplySystemMetadataCommand CreateSystemMetadataCommand(Guid fileId, FileSystemMetadata metadata)
    {
        return new ApplySystemMetadataCommand(
            fileId,
            metadata.Attributes,
            metadata.CreatedUtc.Value,
            metadata.LastWriteUtc.Value,
            metadata.LastAccessUtc.Value,
            metadata.OwnerSid,
            metadata.HardLinkCount,
            metadata.AlternateDataStreamCount);
    }

    private static void CollectError<T>(ParserResult<T> result, ICollection<ApiError> errors)
    {
        if (!result.IsSuccess && result.Error is not null)
        {
            errors.Add(result.Error);
        }
    }

    private static void AddValidationErrors(ICollection<ApiError> errors, ValidationResult validation)
    {
        foreach (var failure in validation.Errors)
        {
            errors.Add(ApiError.ForValue(failure.PropertyName, failure.ErrorMessage));
        }
    }
}

/// <summary>
/// Represents a mapped create-file request with follow-up commands.
/// </summary>
public sealed record CreateFileMappedRequest(
    CreateFileCommand Command,
    FileSystemMetadata? SystemMetadata,
    bool SetReadOnly)
{
    public IEnumerable<IRequest<AppResult<FileSummaryDto>>> BuildFollowUpCommands(Guid fileId)
    {
        if (SystemMetadata is { } metadata)
        {
            yield return new ApplySystemMetadataCommand(
                fileId,
                metadata.Attributes,
                metadata.CreatedUtc.Value,
                metadata.LastWriteUtc.Value,
                metadata.LastAccessUtc.Value,
                metadata.OwnerSid,
                metadata.HardLinkCount,
                metadata.AlternateDataStreamCount);
        }

        if (SetReadOnly)
        {
            yield return new SetFileReadOnlyCommand(fileId, true);
        }
    }
}

/// <summary>
/// Represents mapped metadata updates decomposed into discrete commands.
/// </summary>
public sealed record UpdateFileMetadataMappedRequest(
    UpdateFileMetadataCommand? MetadataCommand,
    ApplySystemMetadataCommand? SystemMetadataCommand,
    SetFileReadOnlyCommand? ReadOnlyCommand)
{
    public IEnumerable<IRequest<AppResult<FileSummaryDto>>> Commands()
    {
        if (MetadataCommand is not null)
        {
            yield return MetadataCommand;
        }

        if (SystemMetadataCommand is not null)
        {
            yield return SystemMetadataCommand;
        }

        if (ReadOnlyCommand is not null)
        {
            yield return ReadOnlyCommand;
        }
    }
}
