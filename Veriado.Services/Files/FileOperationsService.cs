using Veriado.Appl.UseCases.Files.ApplySystemMetadata;
using Veriado.Appl.UseCases.Files.ClearFileValidity;
using Veriado.Appl.UseCases.Files.DeleteFile;
using Veriado.Appl.UseCases.Files.ReplaceFileContent;
using Veriado.Appl.UseCases.Files.RenameFile;
using Veriado.Appl.UseCases.Files.SetFileReadOnly;
using Veriado.Appl.UseCases.Files.SetFileValidity;
using Veriado.Appl.UseCases.Files.UpdateFileContent;
using Veriado.Mapping.AC;

namespace Veriado.Services.Files;

/// <summary>
/// Implements orchestration helpers over file write operations.
/// </summary>
public sealed class FileOperationsService : IFileOperationsService
{
    private readonly IMediator _mediator;
    private readonly WriteMappingPipeline _mappingPipeline;
    private readonly IRequestContext _requestContext;

    public FileOperationsService(IMediator mediator, WriteMappingPipeline mappingPipeline, IRequestContext requestContext)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _mappingPipeline = mappingPipeline ?? throw new ArgumentNullException(nameof(mappingPipeline));
        _requestContext = requestContext ?? throw new ArgumentNullException(nameof(requestContext));
    }

    public async Task<ApiResponse<Guid>> RenameAsync(Guid fileId, string newName, CancellationToken cancellationToken)
    {
        var mapping = await _mappingPipeline.MapRenameAsync(fileId, newName, cancellationToken).ConfigureAwait(false);
        if (!mapping.IsSuccess)
        {
            return ValidationFailure(mapping.Errors);
        }

        using var scope = BeginScope();
        var result = await _mediator.Send(mapping.Data!, cancellationToken).ConfigureAwait(false);
        return ToIdResponse(result);
    }

    public async Task<ApiResponse<Guid>> UpdateMetadataAsync(
        Guid fileId,
        FileMetadataPatchDto patch,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var request = new UpdateMetadataRequest
        {
            FileId = fileId,
            Mime = patch.Mime,
            Author = patch.Author,
            IsReadOnly = patch.IsReadOnly,
            ExpectedVersion = expectedVersion,
        };
        var mapping = await _mappingPipeline.MapUpdateMetadataAsync(request, cancellationToken).ConfigureAwait(false);
        if (!mapping.IsSuccess)
        {
            return ValidationFailure(mapping.Errors);
        }

        using var scope = BeginScope();
        AppResult<FileSummaryDto>? lastResult = null;
        foreach (var command in mapping.Data!.Commands())
        {
            var commandResult = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);
            if (commandResult.IsFailure)
            {
                return ToIdResponse(commandResult);
            }

            lastResult = commandResult;
        }

        if (lastResult is { } finalResult)
        {
            return ToIdResponse(finalResult);
        }

        return ApiResponse<Guid>.Success(request.FileId);
    }

    public async Task<ApiResponse<Guid>> SetReadOnlyAsync(Guid fileId, bool isReadOnly, CancellationToken cancellationToken)
    {
        using var scope = BeginScope();
        var result = await _mediator
            .Send(new SetFileReadOnlyCommand(fileId, isReadOnly), cancellationToken)
            .ConfigureAwait(false);
        return ToIdResponse(result);
    }

    public async Task<ApiResponse<Guid>> SetValidityAsync(
        Guid fileId,
        FileValidityDto validity,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validity);
        var request = new SetValidityRequest
        {
            FileId = fileId,
            IssuedAt = validity.IssuedAt,
            ValidUntil = validity.ValidUntil,
            HasElectronicCopy = validity.HasElectronicCopy,
            HasPhysicalCopy = validity.HasPhysicalCopy,
            ExpectedVersion = expectedVersion,
        };

        var mapping = await _mappingPipeline.MapSetValidityAsync(request, cancellationToken).ConfigureAwait(false);
        if (!mapping.IsSuccess)
        {
            return ValidationFailure(mapping.Errors);
        }

        using var scope = BeginScope();
        var result = await _mediator.Send(mapping.Data!, cancellationToken).ConfigureAwait(false);
        return ToIdResponse(result);
    }

    public async Task<ApiResponse<Guid>> ClearValidityAsync(
        Guid fileId,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        var mapping = await _mappingPipeline
            .MapClearValidityAsync(new ClearValidityRequest { FileId = fileId, ExpectedVersion = expectedVersion }, cancellationToken)
            .ConfigureAwait(false);
        if (!mapping.IsSuccess)
        {
            return ValidationFailure(mapping.Errors);
        }

        using var scope = BeginScope();
        var result = await _mediator.Send(mapping.Data!, cancellationToken).ConfigureAwait(false);
        return ToIdResponse(result);
    }

    public async Task<ApiResponse<Guid>> ReplaceContentAsync(Guid fileId, byte[] content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var request = new ReplaceContentRequest
        {
            FileId = fileId,
            Content = content,
        };

        var mapping = await _mappingPipeline.MapReplaceContentAsync(request, cancellationToken).ConfigureAwait(false);
        if (!mapping.IsSuccess)
        {
            return ValidationFailure(mapping.Errors);
        }

        using var scope = BeginScope();
        var result = await _mediator.Send(mapping.Data!, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return ToIdResponse(result);
        }

        return ToIdResponse(result);
    }

    public Task<ApiResponse<FileSummaryDto>> ReplaceFileContentAsync(
        Guid fileId,
        string sourceFileFullPath,
        CancellationToken cancellationToken)
    {
        return UpdateFileContentAsync(fileId, sourceFileFullPath, cancellationToken);
    }

    public async Task<ApiResponse<FileSummaryDto>> UpdateFileContentAsync(
        Guid fileId,
        string sourceFileFullPath,
        CancellationToken cancellationToken)
    {
        using var scope = BeginScope();
        var result = await _mediator
            .Send(new UpdateFileContentCommand(fileId, sourceFileFullPath), cancellationToken)
            .ConfigureAwait(false);
        return ToResponse(result);
    }

    public async Task<ApiResponse<Guid>> ApplySystemMetadataAsync(Guid fileId, FileSystemMetadataDto metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        using var scope = BeginScope();
        var command = new ApplySystemMetadataCommand(
            fileId,
            (Veriado.Domain.Metadata.FileAttributesFlags)metadata.Attributes,
            metadata.CreatedUtc,
            metadata.LastWriteUtc,
            metadata.LastAccessUtc,
            metadata.OwnerSid,
            metadata.HardLinkCount,
            metadata.AlternateDataStreamCount);

        var result = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);
        return ToIdResponse(result);
    }

    public async Task<ApiResponse<Guid>> DeleteAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (fileId == Guid.Empty)
        {
            return ApiResponse<Guid>.Failure(ApiError.ForValue(nameof(fileId), "File identifier must be a non-empty GUID."));
        }

        using var scope = BeginScope();
        var result = await _mediator.Send(new DeleteFileCommand(fileId), cancellationToken).ConfigureAwait(false);
        return ToIdResponse(result);
    }
    private ApiResponse<Guid> ValidationFailure(IReadOnlyList<ApiError> errors)
    {
        if (errors.Count == 0)
        {
            return ApiResponse<Guid>.Failure(new ApiError("validation_error", "Validation failed."));
        }

        return ApiResponse<Guid>.Failure(errors);
    }

    private static ApiResponse<Guid> ToIdResponse(AppResult<FileSummaryDto> result)
    {
        if (result.IsSuccess)
        {
            return ApiResponse<Guid>.Success(result.Value.Id);
        }

        var error = ConvertAppError(result.Error);
        return ApiResponse<Guid>.Failure(error);
    }

    private static ApiResponse<Guid> ToIdResponse(AppResult<Guid> result)
    {
        if (result.IsSuccess)
        {
            return ApiResponse<Guid>.Success(result.Value);
        }

        var error = ConvertAppError(result.Error);
        return ApiResponse<Guid>.Failure(error);
    }

    private static ApiResponse<FileSummaryDto> ToResponse(AppResult<FileSummaryDto> result)
    {
        if (result.IsSuccess)
        {
            return ApiResponse<FileSummaryDto>.Success(result.Value);
        }

        var error = ConvertAppError(result.Error);
        return ApiResponse<FileSummaryDto>.Failure(error);
    }

    private static ApiError ConvertAppError(AppError error)
    {
        var code = error.Code switch
        {
            ErrorCode.NotFound => "not_found",
            ErrorCode.Conflict => "conflict",
            ErrorCode.Validation => "validation_error",
            ErrorCode.Forbidden => "forbidden",
            ErrorCode.TooLarge => "payload_too_large",
            ErrorCode.Database => "database_error",
            _ => "unexpected_error",
        };

        IReadOnlyDictionary<string, string[]>? details = null;
        if (error.Details is { Count: > 0 })
        {
            details = new Dictionary<string, string[]>
            {
                ["messages"] = error.Details.ToArray(),
            };
        }

        return new ApiError(code, error.Message, null, details);
    }

    private IDisposable BeginScope()
    {
        return AmbientRequestContext.Begin(Guid.NewGuid(), _requestContext.UserId, _requestContext.CorrelationId);
    }
}
