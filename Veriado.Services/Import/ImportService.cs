using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Veriado.Appl.UseCases.Maintenance;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Mapping.AC;
using Veriado.Services.Import.Internal;
using Veriado.Contracts.Import;
using Veriado.Appl.Common;
using Veriado.Appl.Abstractions;

namespace Veriado.Services.Import;

/// <summary>
/// Coordinates high-level import workflows against the application layer.
/// </summary>
public sealed class ImportService : IImportService
{
    private readonly IMediator _mediator;
    private readonly WriteMappingPipeline _mappingPipeline;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;
    private readonly ILogger<ImportService> _logger;

    public ImportService(
        IMediator mediator,
        WriteMappingPipeline mappingPipeline,
        IClock clock,
        IRequestContext requestContext,
        ILogger<ImportService> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _mappingPipeline = mappingPipeline ?? throw new ArgumentNullException(nameof(mappingPipeline));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _requestContext = requestContext ?? throw new ArgumentNullException(nameof(requestContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ApiResponse<Guid>> ImportFileAsync(CreateFileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = NormalizeRequest(request);
        var descriptor = string.IsNullOrWhiteSpace(normalized.Name) ? normalized.Extension : normalized.Name;
        return await ImportFileInternalAsync(normalized, descriptor, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ApiResponse<ImportBatchResult>> ImportFolderAsync(ImportFolderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.FolderPath) || !Directory.Exists(request.FolderPath))
        {
            var error = new ApiError("folder_not_found", $"Folder '{request.FolderPath}' was not found.", nameof(request.FolderPath));
            return ApiResponse<ImportBatchResult>.Failure(error);
        }

        var searchPattern = string.IsNullOrWhiteSpace(request.SearchPattern) ? "*" : request.SearchPattern!;
        var searchOption = request.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var errors = new ConcurrentBag<ImportError>();
        var total = 0;
        var succeeded = 0;
        var fatalEncountered = false;

        try
        {
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = request.MaxDegreeOfParallelism > 0
                    ? request.MaxDegreeOfParallelism
                    : 1,
            };

            await Parallel.ForEachAsync(
                Directory.EnumerateFiles(request.FolderPath, searchPattern, searchOption),
                parallelOptions,
                async (filePath, token) =>
                {
                    Interlocked.Increment(ref total);

                    try
                    {
                        var createRequest = await CreateRequestFromFileAsync(filePath, request, token).ConfigureAwait(false);
                        var response = await ImportFileInternalAsync(createRequest, filePath, token)
                            .ConfigureAwait(false);

                        if (response.IsSuccess)
                        {
                            Interlocked.Increment(ref succeeded);
                            return;
                        }

                        RecordErrors(filePath, response.Errors, errors);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to import file {FilePath}", filePath);
                        errors.Add(new ImportError(filePath, "unexpected_error", ex.Message));
                    }
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            fatalEncountered = true;
            errors.Add(new ImportError(request.FolderPath, "canceled", "The import operation was canceled."));
        }
        catch (Exception ex)
        {
            fatalEncountered = true;
            _logger.LogError(ex, "Folder import failed for {FolderPath}", request.FolderPath);
            errors.Add(new ImportError(request.FolderPath, "unexpected_error", ex.Message));
        }

        var processed = total;
        var failed = Math.Max(0, processed - succeeded);
        var errorArray = errors.ToArray();
        var status = DetermineStatus(processed, succeeded, failed, fatalEncountered, errorArray);
        var batchResult = new ImportBatchResult(status, processed, succeeded, failed, errorArray);
        return ApiResponse<ImportBatchResult>.Success(batchResult);
    }

    private static ImportBatchStatus DetermineStatus(
        int total,
        int succeeded,
        int failed,
        bool fatalEncountered,
        IReadOnlyCollection<ImportError> errors)
    {
        if (fatalEncountered)
        {
            return ImportBatchStatus.FatalError;
        }

        if (total == 0 && errors.Count == 0)
        {
            return ImportBatchStatus.Success;
        }

        if (failed == 0)
        {
            return ImportBatchStatus.Success;
        }

        if (succeeded == 0)
        {
            return ImportBatchStatus.Failure;
        }

        return ImportBatchStatus.PartialSuccess;
    }

    private async Task<ApiResponse<Guid>> ImportFileInternalAsync(
        CreateFileRequest request,
        string? descriptor,
        CancellationToken cancellationToken)
    {
        var mapping = await _mappingPipeline.MapCreateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!mapping.IsSuccess)
        {
            LogApiErrors(descriptor, mapping.Errors);
            return ApiResponse<Guid>.Failure(mapping.Errors);
        }

        using var scope = BeginScope();

        var mapped = mapping.Data!;
        var createResult = await _mediator.Send(mapped.Command, cancellationToken).ConfigureAwait(false);
        if (createResult.IsFailure)
        {
            var apiError = ConvertAppError(createResult.Error);
            LogApiError(descriptor, apiError);
            return ApiResponse<Guid>.Failure(apiError);
        }

        var fileId = createResult.Value;

        foreach (var followUp in mapped.BuildFollowUpCommands(fileId))
        {
            var followUpResult = await _mediator.Send(followUp, cancellationToken).ConfigureAwait(false);
            if (followUpResult.IsFailure)
            {
                var apiError = ConvertAppError(followUpResult.Error);
                LogApiError(descriptor, apiError);
                return ApiResponse<Guid>.Failure(apiError);
            }
        }

        return ApiResponse<Guid>.Success(fileId);
    }

    private async Task<CreateFileRequest> CreateRequestFromFileAsync(
        string filePath,
        ImportFolderRequest options,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        var extensionWithDot = Path.GetExtension(filePath);
        var extension = string.IsNullOrWhiteSpace(extensionWithDot) ? string.Empty : extensionWithDot.TrimStart('.');
        var name = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileName(filePath);
        }

        var author = string.IsNullOrWhiteSpace(options.DefaultAuthor) ? string.Empty : options.DefaultAuthor;
        FileSystemMetadataDto? systemMetadata = null;
        var isReadOnly = options.SetReadOnly;

        try
        {
            var info = new FileInfo(filePath);
            info.Refresh();
            if (options.KeepFsMetadata)
            {
                systemMetadata = new FileSystemMetadataDto(
                    (int)info.Attributes,
                    CoerceToUtc(info.CreationTimeUtc),
                    CoerceToUtc(info.LastWriteTimeUtc),
                    CoerceToUtc(info.LastAccessTimeUtc),
                    OwnerSid: null,
                    HardLinkCount: null,
                    AlternateDataStreamCount: null);
            }

            if (!options.SetReadOnly)
            {
                isReadOnly = info.IsReadOnly;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogDebug(ex, "Failed to capture file metadata for {FilePath}", filePath);
        }

        return NormalizeRequest(new CreateFileRequest
        {
            Name = name,
            Extension = extension,
            Mime = MimeMap.GetMimeType(extension),
            Author = author,
            Content = bytes,
            SystemMetadata = systemMetadata,
            IsReadOnly = isReadOnly,
        });
    }

    private CreateFileRequest NormalizeRequest(CreateFileRequest request)
    {
        var normalizedExtension = (request.Extension ?? string.Empty).TrimStart('.');
        var mime = string.IsNullOrWhiteSpace(request.Mime)
            ? MimeMap.GetMimeType(normalizedExtension)
            : request.Mime!;

        return new CreateFileRequest
        {
            Name = request.Name ?? string.Empty,
            Extension = normalizedExtension,
            Mime = mime,
            Author = request.Author ?? string.Empty,
            Content = request.Content ?? Array.Empty<byte>(),
            MaxContentLength = request.MaxContentLength,
            SystemMetadata = request.SystemMetadata,
            IsReadOnly = request.IsReadOnly,
        };
    }

    private void RecordErrors(string filePath, IReadOnlyList<ApiError> errors, ConcurrentBag<ImportError> sink)
    {
        foreach (var error in errors)
        {
            sink.Add(new ImportError(filePath, error.Code, error.Message));
            _logger.LogError("Failed to import file {FilePath}: {ErrorCode} - {Message}", filePath, error.Code, error.Message);
        }
    }

    private void LogApiErrors(string? descriptor, IReadOnlyList<ApiError> errors)
    {
        foreach (var error in errors)
        {
            LogApiError(descriptor, error);
        }
    }

    private void LogApiError(string? descriptor, ApiError error)
    {
        if (!string.IsNullOrWhiteSpace(descriptor))
        {
            _logger.LogError("Import failed for {Descriptor}: {ErrorCode} - {Message}", descriptor, error.Code, error.Message);
            return;
        }

        _logger.LogError("Import failed: {ErrorCode} - {Message}", error.Code, error.Message);
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

    private DateTimeOffset CoerceToUtc(DateTime value)
    {
        if (value == DateTime.MinValue || value == DateTime.MaxValue)
        {
            return _clock.UtcNow;
        }

        if (value.Kind == DateTimeKind.Unspecified)
        {
            value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
        else if (value.Kind == DateTimeKind.Local)
        {
            value = value.ToUniversalTime();
        }

        return new DateTimeOffset(value, TimeSpan.Zero);
    }
}
