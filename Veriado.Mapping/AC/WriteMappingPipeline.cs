using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Veriado.Application.Files.Commands;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Mapping.AC;

/// <summary>
/// Coordinates DTO-to-command mapping, value object parsing and validation.
/// </summary>
public sealed class WriteMappingPipeline
{
    private readonly IValidator<CreateFileCommand> _createValidator;
    private readonly IValidator<ReplaceContentCommand> _replaceValidator;
    private readonly IValidator<UpdateMetadataCommand> _updateValidator;
    private readonly IValidator<RenameFileCommand> _renameValidator;
    private readonly IValidator<SetValidityCommand> _setValidityValidator;
    private readonly IValidator<ClearValidityCommand> _clearValidityValidator;

    /// <summary>
    /// Initializes a new instance of the <see cref="WriteMappingPipeline"/> class.
    /// </summary>
    public WriteMappingPipeline(
        IValidator<CreateFileCommand> createValidator,
        IValidator<ReplaceContentCommand> replaceValidator,
        IValidator<UpdateMetadataCommand> updateValidator,
        IValidator<RenameFileCommand> renameValidator,
        IValidator<SetValidityCommand> setValidityValidator,
        IValidator<ClearValidityCommand> clearValidityValidator)
    {
        _createValidator = createValidator ?? throw new ArgumentNullException(nameof(createValidator));
        _replaceValidator = replaceValidator ?? throw new ArgumentNullException(nameof(replaceValidator));
        _updateValidator = updateValidator ?? throw new ArgumentNullException(nameof(updateValidator));
        _renameValidator = renameValidator ?? throw new ArgumentNullException(nameof(renameValidator));
        _setValidityValidator = setValidityValidator ?? throw new ArgumentNullException(nameof(setValidityValidator));
        _clearValidityValidator = clearValidityValidator ?? throw new ArgumentNullException(nameof(clearValidityValidator));
    }

    /// <summary>
    /// Maps a create request to a validated command.
    /// </summary>
    public async Task<ApiResponse<CreateFileCommand>> MapCreateAsync(CreateFileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<ApiError>();

        var nameResult = Parsers.ParseFileName(request.Name, nameof(request.Name));
        var extensionResult = Parsers.ParseFileExtension(request.Extension, nameof(request.Extension));
        var mimeResult = Parsers.ParseMimeType(request.Mime, nameof(request.Mime));
        var metadataPatches = Parsers.ParseMetadataPatches(request.ExtendedMetadata, nameof(request.ExtendedMetadata), errors);
        var systemMetadata = Parsers.ParseOptionalMetadata(request.SystemMetadata, nameof(request.SystemMetadata), errors);

        CollectError(nameResult, errors);
        CollectError(extensionResult, errors);
        CollectError(mimeResult, errors);

        if (errors.Count > 0)
        {
            return ApiResponse<CreateFileCommand>.Failure(errors);
        }

        var command = new CreateFileCommand(
            nameResult.Value,
            extensionResult.Value,
            mimeResult.Value,
            request.Author ?? string.Empty,
            request.Content ?? Array.Empty<byte>(),
            request.MaxContentLength,
            metadataPatches,
            systemMetadata,
            request.IsReadOnly);

        var validation = await _createValidator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            AddValidationErrors(errors, validation);
            return ApiResponse<CreateFileCommand>.Failure(errors);
        }

        return ApiResponse<CreateFileCommand>.Success(command);
    }

    /// <summary>
    /// Maps a replace content request to a validated command.
    /// </summary>
    public async Task<ApiResponse<ReplaceContentCommand>> MapReplaceContentAsync(ReplaceContentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new ReplaceContentCommand(request.FileId, request.Content ?? Array.Empty<byte>(), request.MaxContentLength);
        var validation = await _replaceValidator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            var errors = new List<ApiError>();
            AddValidationErrors(errors, validation);
            return ApiResponse<ReplaceContentCommand>.Failure(errors);
        }

        return ApiResponse<ReplaceContentCommand>.Success(command);
    }

    /// <summary>
    /// Maps an update metadata request to a validated command.
    /// </summary>
    public async Task<ApiResponse<UpdateMetadataCommand>> MapUpdateMetadataAsync(UpdateMetadataRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<ApiError>();

        MimeType? mime = null;
        if (!string.IsNullOrWhiteSpace(request.Mime))
        {
            var mimeResult = Parsers.ParseMimeType(request.Mime, nameof(request.Mime));
            if (!mimeResult.IsSuccess)
            {
                errors.Add(mimeResult.Error!);
            }
            else
            {
                mime = mimeResult.Value;
            }
        }

        var systemMetadata = Parsers.ParseOptionalMetadata(request.SystemMetadata, nameof(request.SystemMetadata), errors);
        var metadataPatches = Parsers.ParseMetadataPatches(request.ExtendedMetadata, nameof(request.ExtendedMetadata), errors);

        if (errors.Count > 0)
        {
            return ApiResponse<UpdateMetadataCommand>.Failure(errors);
        }

        var command = new UpdateMetadataCommand(
            request.FileId,
            mime,
            request.Author,
            request.IsReadOnly,
            systemMetadata,
            metadataPatches);

        var validation = await _updateValidator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            AddValidationErrors(errors, validation);
            return ApiResponse<UpdateMetadataCommand>.Failure(errors);
        }

        return ApiResponse<UpdateMetadataCommand>.Success(command);
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

        var command = new RenameFileCommand(fileId, nameResult.Value);
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
    public async Task<ApiResponse<SetValidityCommand>> MapSetValidityAsync(SetValidityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var issuedResult = Parsers.ParseUtcTimestamp(request.IssuedAt);
        var untilResult = Parsers.ParseUtcTimestamp(request.ValidUntil);
        var errors = new List<ApiError>();
        CollectError(issuedResult, errors);
        CollectError(untilResult, errors);

        if (errors.Count > 0)
        {
            return ApiResponse<SetValidityCommand>.Failure(errors);
        }

        var command = new SetValidityCommand(
            request.FileId,
            issuedResult.Value,
            untilResult.Value,
            request.HasPhysicalCopy,
            request.HasElectronicCopy);

        var validation = await _setValidityValidator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            AddValidationErrors(errors, validation);
            return ApiResponse<SetValidityCommand>.Failure(errors);
        }

        return ApiResponse<SetValidityCommand>.Success(command);
    }

    /// <summary>
    /// Maps a clear-validity request to a validated command.
    /// </summary>
    public async Task<ApiResponse<ClearValidityCommand>> MapClearValidityAsync(ClearValidityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new ClearValidityCommand(request.FileId);
        var validation = await _clearValidityValidator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            var errors = new List<ApiError>();
            AddValidationErrors(errors, validation);
            return ApiResponse<ClearValidityCommand>.Failure(errors);
        }

        return ApiResponse<ClearValidityCommand>.Success(command);
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
