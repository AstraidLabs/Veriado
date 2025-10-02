using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Veriado.Mapping.AC;
using Veriado.Services.Import.Internal;
using Veriado.Contracts.Import;
using Veriado.Appl.UseCases.Files.CheckFileHash;

namespace Veriado.Services.Import;

/// <summary>
/// Coordinates high-level import workflows against the application layer.
/// </summary>
public sealed class ImportService : IImportService
{
    private static readonly char[] InvalidSearchPatternCharacters = Path
        .GetInvalidFileNameChars()
        .Where(static c => c is not '*' and not '?')
        .Distinct()
        .ToArray();

    private static readonly char[] SearchPatternPathSeparators =
    {
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar,
    };

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
    [Obsolete("Use the streaming import APIs.")]
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
    [Obsolete("Use ImportFolderStreamAsync instead.")]
    public async Task<ApiResponse<ImportBatchResult>> ImportFolderAsync(ImportFolderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.FolderPath) || !Directory.Exists(request.FolderPath))
        {
            var error = new ApiError("folder_not_found", $"Folder '{request.FolderPath}' was not found.", nameof(request.FolderPath));
            return ApiResponse<ImportBatchResult>.Failure(error);
        }

        var normalized = NormalizeOptions(request);
        ImportAggregateResult? aggregate = null;

        await foreach (var progress in ImportFolderStreamCoreAsync(request.FolderPath, normalized, cancellationToken).ConfigureAwait(false))
        {
            if (progress.Kind == ImportProgressKind.BatchCompleted && progress.Aggregate is not null)
            {
                aggregate = progress.Aggregate;
            }
        }

        aggregate ??= ImportAggregateResult.EmptySuccess;

        var batchResult = new ImportBatchResult(
            aggregate.Status,
            aggregate.Total,
            aggregate.Processed,
            aggregate.Succeeded,
            aggregate.Failed,
            aggregate.Skipped,
            aggregate.Errors);

        if (aggregate.Status == ImportBatchStatus.FatalError)
        {
            var firstError = aggregate.Errors.FirstOrDefault();
            var message = firstError is null
                ? "Import was stopped due to a fatal error. Resolve the issue and try again."
                : $"Import was stopped due to a fatal error: {firstError.Message}";
            var code = string.IsNullOrWhiteSpace(firstError?.Code) ? "import_fatal_error" : firstError!.Code;
            return ApiResponse<ImportBatchResult>.Failure(new ApiError(code, message, firstError?.FilePath));
        }

        return ApiResponse<ImportBatchResult>.Success(batchResult);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ImportProgressEvent> ImportFolderStreamAsync(
        string folderPath,
        ImportOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = NormalizeOptions(options);

        await foreach (var progress in ImportFolderStreamCoreAsync(folderPath, normalized, cancellationToken).ConfigureAwait(false))
        {
            yield return progress;
        }
    }

    private static ImportBatchStatus DetermineStatus(
        int total,
        int processed,
        int succeeded,
        int failed,
        int skipped,
        bool fatalEncountered,
        bool cancellationEncountered,
        IReadOnlyCollection<ImportError> errors)
    {
        if (fatalEncountered)
        {
            return ImportBatchStatus.FatalError;
        }

        if (cancellationEncountered)
        {
            return ImportBatchStatus.PartialSuccess;
        }

        if (total == 0 && errors.Count == 0)
        {
            return ImportBatchStatus.Success;
        }

        var accounted = succeeded + failed + skipped;
        if (accounted < total)
        {
            return ImportBatchStatus.PartialSuccess;
        }

        if (failed == 0)
        {
            return ImportBatchStatus.Success;
        }

        if (succeeded == 0 && skipped == 0)
        {
            return ImportBatchStatus.Failure;
        }

        return ImportBatchStatus.PartialSuccess;
    }

    private async IAsyncEnumerable<ImportProgressEvent> ImportFolderStreamCoreAsync(
        string folderPath,
        NormalizedImportOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folderPath))
        {
            var missingError = new ImportError(
                folderPath,
                "folder_not_found",
                $"Folder '{folderPath}' was not found.",
                "Verify the folder path and try again.",
                null,
                _clock.UtcNow);
            var fatalAggregate = new ImportAggregateResult(ImportBatchStatus.FatalError, 0, 0, 0, 0, 0, new[] { missingError });
            var now = _clock.UtcNow;

            yield return ImportProgressEvent.BatchStarted(0, now);
            yield return ImportProgressEvent.ErrorOccurred(missingError, 0, 0, 0, 0, 0, now);
            yield return ImportProgressEvent.BatchCompleted(fatalAggregate, _clock.UtcNow);
            yield break;
        }

        if (!TryValidateSearchPattern(folderPath, options, out var validationError))
        {
            var fatalAggregate = new ImportAggregateResult(
                ImportBatchStatus.FatalError,
                0,
                0,
                0,
                0,
                0,
                new[] { validationError });
            var startedAt = validationError.Timestamp;

            yield return ImportProgressEvent.BatchStarted(0, startedAt);
            yield return ImportProgressEvent.ErrorOccurred(validationError, 0, 0, 0, 0, 0);
            yield return ImportProgressEvent.BatchCompleted(fatalAggregate, _clock.UtcNow);
            yield break;
        }

        var searchOption = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        string[] files;
        ImportProgressEvent[]? enumerationFailureEvents = null;
        try
        {
            files = Directory.EnumerateFiles(folderPath, options.SearchPattern, searchOption).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException)
        {
            _logger.LogError(ex, "Failed to enumerate files for {FolderPath} using pattern {SearchPattern}", folderPath, options.SearchPattern);

            var enumerationError = CreateValidationError(
                folderPath,
                "enumeration_failed",
                $"Failed to enumerate files in '{folderPath}' using pattern '{options.SearchPattern}'. {ex.Message}",
                "Verify that the folder is accessible and that the search pattern is valid.");
            var fatalAggregate = new ImportAggregateResult(
                ImportBatchStatus.FatalError,
                0,
                0,
                0,
                0,
                0,
                new[] { enumerationError });

            enumerationFailureEvents = new[]
            {
                ImportProgressEvent.BatchStarted(0, enumerationError.Timestamp),
                ImportProgressEvent.ErrorOccurred(enumerationError, 0, 0, 0, 0, 0),
                ImportProgressEvent.BatchCompleted(fatalAggregate, _clock.UtcNow),
            };

            files = Array.Empty<string>();
        }

        if (enumerationFailureEvents is not null)
        {
            foreach (var progressEvent in enumerationFailureEvents)
            {
                yield return progressEvent;
            }

            yield break;
        }

        var total = files.Length;
        var batchStart = _clock.UtcNow;

        yield return ImportProgressEvent.BatchStarted(total, batchStart);

        if (total == 0)
        {
            yield return ImportProgressEvent.BatchCompleted(ImportAggregateResult.EmptySuccess, _clock.UtcNow);
            yield break;
        }

        var channelCapacity = Math.Clamp(options.MaxDegreeOfParallelism * 4, 8, 256);
        var channel = Channel.CreateBounded<ImportProgressEvent>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        var errors = new ConcurrentBag<ImportError>();
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var fatalEncountered = false;
        var cancellationEncountered = false;

        var writerTask = Task.Run(async () =>
        {
            try
            {
                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                };

                await Parallel.ForEachAsync(files, parallelOptions, async (filePath, token) =>
                {
                    token.ThrowIfCancellationRequested();

                    await channel.Writer.WriteAsync(
                        ImportProgressEvent.FileStarted(filePath, Volatile.Read(ref processed), total, _clock.UtcNow),
                        CancellationToken.None).ConfigureAwait(false);

                    int successCountSnapshot;
                    int failedCountSnapshot;

                    try
                    {
                        var createRequest = await CreateRequestFromFileAsync(filePath, options, token).ConfigureAwait(false);

                        if (await FileAlreadyExistsAsync(createRequest.ContentHash, token).ConfigureAwait(false))
                        {
                            _logger.LogInformation(
                                "Skipping file {FilePath} because identical content already exists (SHA256 {Hash}).",
                                filePath,
                                createRequest.ContentHash);

                            var completedSkip = Interlocked.Increment(ref processed);
                            var skippedSnapshot = Interlocked.Increment(ref skipped);
                            successCountSnapshot = Volatile.Read(ref succeeded);
                            failedCountSnapshot = Volatile.Read(ref failed);
                            var skipMessage = $"Skipped '{filePath}' because identical content already exists.";

                            await channel.Writer.WriteAsync(
                                ImportProgressEvent.FileCompleted(
                                    filePath,
                                    completedSkip,
                                    total,
                                    successCountSnapshot,
                                    skippedSnapshot,
                                    skipMessage,
                                    _clock.UtcNow),
                                CancellationToken.None).ConfigureAwait(false);

                            await channel.Writer.WriteAsync(
                                ImportProgressEvent.Progress(
                                    completedSkip,
                                    total,
                                    successCountSnapshot,
                                    failedCountSnapshot,
                                    skippedSnapshot,
                                    skipMessage,
                                    _clock.UtcNow),
                                CancellationToken.None).ConfigureAwait(false);

                            return;
                        }

                        var response = await ImportFileInternalAsync(createRequest.Request, filePath, token).ConfigureAwait(false);

                        if (response.IsSuccess)
                        {
                            var completed = Interlocked.Increment(ref processed);
                            var success = Interlocked.Increment(ref succeeded);
                            var skippedSnapshot = Volatile.Read(ref skipped);

                            await channel.Writer.WriteAsync(
                                ImportProgressEvent.FileCompleted(
                                    filePath,
                                    completed,
                                    total,
                                    success,
                                    skippedSnapshot,
                                    timestamp: _clock.UtcNow),
                                CancellationToken.None).ConfigureAwait(false);

                            await channel.Writer.WriteAsync(
                                ImportProgressEvent.Progress(
                                    completed,
                                    total,
                                    success,
                                    Volatile.Read(ref failed),
                                    skippedSnapshot,
                                    timestamp: _clock.UtcNow),
                                CancellationToken.None).ConfigureAwait(false);

                            return;
                        }

                        if (IsDuplicateConflict(response.Errors))
                        {
                            _logger.LogInformation(
                                "Skipping file {FilePath} because identical content already exists (detected after write conflict).",
                                filePath);

                            var completedSkip = Interlocked.Increment(ref processed);
                            var skippedSnapshot = Interlocked.Increment(ref skipped);
                            successCountSnapshot = Volatile.Read(ref succeeded);
                            failedCountSnapshot = Volatile.Read(ref failed);
                            var skipMessage = $"Skipped '{filePath}' because identical content already exists.";

                            await channel.Writer.WriteAsync(
                                ImportProgressEvent.FileCompleted(
                                    filePath,
                                    completedSkip,
                                    total,
                                    successCountSnapshot,
                                    skippedSnapshot,
                                    skipMessage,
                                    _clock.UtcNow),
                                CancellationToken.None).ConfigureAwait(false);

                            await channel.Writer.WriteAsync(
                                ImportProgressEvent.Progress(
                                    completedSkip,
                                    total,
                                    successCountSnapshot,
                                    failedCountSnapshot,
                                    skippedSnapshot,
                                    skipMessage,
                                    _clock.UtcNow),
                                CancellationToken.None).ConfigureAwait(false);

                            return;
                        }

                        var importErrors = CreateImportErrors(filePath, response.Errors);
                        foreach (var error in importErrors)
                        {
                            errors.Add(error);
                            _logger.LogError(
                                "Failed to import file {FilePath}: {ErrorCode} - {Message}",
                                filePath,
                                error.Code,
                                error.Message);
                        }

                        var primaryError = importErrors.First();
                        var completedFailure = Interlocked.Increment(ref processed);
                        successCountSnapshot = Volatile.Read(ref succeeded);
                        failedCountSnapshot = Interlocked.Increment(ref failed);
                        var skippedSnapshot = Volatile.Read(ref skipped);

                        await channel.Writer.WriteAsync(
                            ImportProgressEvent.ErrorOccurred(
                                primaryError,
                                completedFailure,
                                total,
                                successCountSnapshot,
                                failedCountSnapshot,
                                skippedSnapshot,
                                _clock.UtcNow),
                            CancellationToken.None).ConfigureAwait(false);

                        await channel.Writer.WriteAsync(
                            ImportProgressEvent.Progress(
                                completedFailure,
                                total,
                                successCountSnapshot,
                                failedCountSnapshot,
                                skippedSnapshot,
                                timestamp: _clock.UtcNow),
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (FileTooLargeException ex)
                    {
                        var error = new ImportError(
                            ex.FilePath,
                            "file_too_large",
                            $"File size {ex.ActualSizeBytes} bytes exceeds the configured maximum of {ex.MaxAllowedBytes} bytes.",
                            "Reduce the file size or increase the configured maximum.",
                            null,
                            _clock.UtcNow);
                        errors.Add(error);

                        _logger.LogWarning(
                            "Skipping file {FilePath} because it exceeds the configured maximum size (actual {Actual} bytes, limit {Limit} bytes).",
                            filePath,
                            ex.ActualSizeBytes,
                            ex.MaxAllowedBytes);

                        var completedFailure = Interlocked.Increment(ref processed);
                        successCountSnapshot = Volatile.Read(ref succeeded);
                        failedCountSnapshot = Interlocked.Increment(ref failed);
                        var skippedSnapshot = Volatile.Read(ref skipped);

                        await channel.Writer.WriteAsync(
                            ImportProgressEvent.ErrorOccurred(
                                error,
                                completedFailure,
                                total,
                                successCountSnapshot,
                                failedCountSnapshot,
                                skippedSnapshot,
                                _clock.UtcNow),
                            CancellationToken.None).ConfigureAwait(false);

                        await channel.Writer.WriteAsync(
                            ImportProgressEvent.Progress(
                                completedFailure,
                                total,
                                successCountSnapshot,
                                failedCountSnapshot,
                                skippedSnapshot,
                                timestamp: _clock.UtcNow),
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        var error = new ImportError(
                            filePath,
                            "unexpected_error",
                            ex.Message,
                            "See application logs for details.",
                            ex.ToString(),
                            _clock.UtcNow);
                        errors.Add(error);

                        _logger.LogError(ex, "Failed to import file {FilePath}", filePath);

                        var completedFailure = Interlocked.Increment(ref processed);
                        successCountSnapshot = Volatile.Read(ref succeeded);
                        failedCountSnapshot = Interlocked.Increment(ref failed);
                        var skippedSnapshot = Volatile.Read(ref skipped);

                        await channel.Writer.WriteAsync(
                            ImportProgressEvent.ErrorOccurred(
                                error,
                                completedFailure,
                                total,
                                successCountSnapshot,
                                failedCountSnapshot,
                                skippedSnapshot,
                                _clock.UtcNow),
                            CancellationToken.None).ConfigureAwait(false);

                        await channel.Writer.WriteAsync(
                            ImportProgressEvent.Progress(
                                completedFailure,
                                total,
                                successCountSnapshot,
                                failedCountSnapshot,
                                skippedSnapshot,
                                timestamp: _clock.UtcNow),
                            CancellationToken.None).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancellationEncountered = true;
                var error = new ImportError(
                    folderPath,
                    "canceled",
                    "The import operation was canceled.",
                    "Restart the import when ready.",
                    null,
                    _clock.UtcNow);
                errors.Add(error);

                _logger.LogInformation("Import canceled for {FolderPath}", folderPath);

                await channel.Writer.WriteAsync(
                    ImportProgressEvent.ErrorOccurred(
                        error,
                        Volatile.Read(ref processed),
                        total,
                        Volatile.Read(ref succeeded),
                        Volatile.Read(ref failed),
                        Volatile.Read(ref skipped),
                        _clock.UtcNow),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                fatalEncountered = true;
                var error = new ImportError(
                    folderPath,
                    "unexpected_error",
                    ex.Message,
                    "See application logs for details.",
                    ex.ToString(),
                    _clock.UtcNow);
                errors.Add(error);

                _logger.LogError(ex, "Folder import failed for {FolderPath}", folderPath);

                await channel.Writer.WriteAsync(
                    ImportProgressEvent.ErrorOccurred(
                        error,
                        Volatile.Read(ref processed),
                        total,
                        Volatile.Read(ref succeeded),
                        Volatile.Read(ref failed),
                        Volatile.Read(ref skipped),
                        _clock.UtcNow),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        await foreach (var progress in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return progress;
        }

        await writerTask.ConfigureAwait(false);

        var processedFinal = Volatile.Read(ref processed);
        var succeededFinal = Volatile.Read(ref succeeded);
        var failedFinal = Volatile.Read(ref failed);
        var skippedFinal = Volatile.Read(ref skipped);
        var errorArray = errors.ToArray();
        var status = DetermineStatus(
            total,
            processedFinal,
            succeededFinal,
            failedFinal,
            skippedFinal,
            fatalEncountered,
            cancellationEncountered,
            errorArray);
        var aggregate = new ImportAggregateResult(status, total, processedFinal, succeededFinal, failedFinal, skippedFinal, errorArray);

        yield return ImportProgressEvent.BatchCompleted(aggregate, _clock.UtcNow);
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

    private async Task<CreateFileImport> CreateRequestFromFileAsync(
        string filePath,
        NormalizedImportOptions options,
        CancellationToken cancellationToken)
    {
        var maxFileSizeBytes = options.MaxFileSizeBytes;
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
            if (maxFileSizeBytes.HasValue && info.Exists && info.Length > maxFileSizeBytes.Value)
            {
                throw new FileTooLargeException(filePath, info.Length, maxFileSizeBytes.Value);
            }

            if (options.KeepFileSystemMetadata && info.Exists)
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
                isReadOnly = info.Exists && info.IsReadOnly;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogDebug(ex, "Failed to capture file metadata for {FilePath}", filePath);
        }

        var contentResult = await ReadFileContentAsync(filePath, options, cancellationToken).ConfigureAwait(false);
        if (maxFileSizeBytes.HasValue && contentResult.Content.LongLength > maxFileSizeBytes.Value)
        {
            throw new FileTooLargeException(filePath, contentResult.Content.LongLength, maxFileSizeBytes.Value);
        }

        var request = NormalizeRequest(new CreateFileRequest
        {
            Name = name,
            Extension = extension,
            Mime = MimeMap.GetMimeType(extension),
            Author = author,
            Content = contentResult.Content,
            MaxContentLength = maxFileSizeBytes.HasValue && maxFileSizeBytes.Value <= int.MaxValue
                ? (int?)maxFileSizeBytes.Value
                : null,
            SystemMetadata = systemMetadata,
            IsReadOnly = isReadOnly,
        });

        return new CreateFileImport(request, contentResult.Hash);
    }

    private async Task<FileContentReadResult> ReadFileContentAsync(
        string filePath,
        NormalizedImportOptions options,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            options.BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (options.MaxFileSizeBytes.HasValue && stream.Length > options.MaxFileSizeBytes.Value)
        {
            throw new FileTooLargeException(filePath, stream.Length, options.MaxFileSizeBytes.Value);
        }

        if (stream.Length > int.MaxValue)
        {
            throw new FileTooLargeException(filePath, stream.Length, int.MaxValue);
        }

        var size = (int)stream.Length;
        if (size == 0)
        {
            using var emptyHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var emptyHashValue = emptyHash.GetHashAndReset();
            var emptyHashText = Convert.ToHexString(emptyHashValue);
            _logger.LogDebug("Read {Length} bytes from {FilePath} (SHA256 {Hash}).", 0, filePath, emptyHashText);
            return new FileContentReadResult(Array.Empty<byte>(), emptyHashText);
        }

        var content = new byte[size];
        var buffer = ArrayPool<byte>.Shared.Rent(options.BufferSize);
        var offset = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        try
        {
            while (offset < size)
            {
                var toRead = Math.Min(buffer.Length, size - offset);
                var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                buffer.AsSpan(0, read).CopyTo(content.AsSpan(offset));
                hash.AppendData(buffer, 0, read);
                offset += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (offset < size)
        {
            Array.Resize(ref content, offset);
        }

        var hashValue = hash.GetHashAndReset();
        var hashText = Convert.ToHexString(hashValue);
        _logger.LogDebug("Read {Length} bytes from {FilePath} (SHA256 {Hash}).", offset, filePath, hashText);

        return new FileContentReadResult(content, hashText);
    }

    private CreateFileRequest NormalizeRequest(CreateFileRequest request)
    {
        var normalizedExtension = (request.Extension ?? string.Empty).TrimStart('.');
        var mime = string.IsNullOrWhiteSpace(request.Mime)
            ? MimeMap.GetMimeType(normalizedExtension)
            : request.Mime!;

        var normalizedAuthor = request.Author is null ? string.Empty : request.Author.Trim();

        return new CreateFileRequest
        {
            Name = request.Name ?? string.Empty,
            Extension = normalizedExtension,
            Mime = mime,
            Author = normalizedAuthor,
            Content = request.Content ?? Array.Empty<byte>(),
            MaxContentLength = request.MaxContentLength,
            SystemMetadata = request.SystemMetadata,
            IsReadOnly = request.IsReadOnly,
        };
    }

    private static bool IsDuplicateConflict(IReadOnlyList<ApiError> errors)
    {
        if (errors is not { Count: > 0 })
        {
            return false;
        }

        return errors.All(static error =>
            string.Equals(error.Code, "conflict", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(error.Message)
            && error.Message.Contains("identical content", StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<ImportError> CreateImportErrors(string filePath, IReadOnlyList<ApiError> errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return new[]
            {
                new ImportError(
                    filePath,
                    "unexpected_error",
                    "Import failed due to an unknown error.",
                    null,
                    null,
                    _clock.UtcNow),
            };
        }

        var result = new List<ImportError>(errors.Count);
        foreach (var error in errors)
        {
            result.Add(CreateImportError(filePath, error));
        }

        return result;
    }

    private ImportError CreateImportError(string filePath, ApiError error)
    {
        var suggestion = BuildSuggestion(error);
        return new ImportError(filePath, error.Code, error.Message, suggestion, null, _clock.UtcNow);
    }

    private static string? BuildSuggestion(ApiError error)
    {
        if (error.Details is not { Count: > 0 })
        {
            return null;
        }

        var lines = new List<string>();
        foreach (var detail in error.Details)
        {
            var values = detail.Value is { Length: > 0 }
                ? string.Join(", ", detail.Value)
                : string.Empty;

            if (string.IsNullOrWhiteSpace(detail.Key))
            {
                lines.Add(values);
            }
            else
            {
                lines.Add($"{detail.Key}: {values}");
            }
        }

        return string.Join(Environment.NewLine, lines.Where(static line => !string.IsNullOrWhiteSpace(line)));
    }


    private bool TryValidateSearchPattern(
        string folderPath,
        NormalizedImportOptions options,
        [NotNullWhen(false)] out ImportError? validationError)
    {
        var pattern = options.SearchPattern;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            validationError = CreateValidationError(
                folderPath,
                "invalid_search_pattern",
                "Search pattern cannot be empty.",
                "Provide a value such as '*' or '*.txt'.");
            return false;
        }

        if (pattern.IndexOfAny(SearchPatternPathSeparators) >= 0)
        {
            validationError = CreateValidationError(
                folderPath,
                "invalid_search_pattern",
                "Search pattern must not contain directory separators.",
                "Remove '/' or '\\' characters from the search pattern.");
            return false;
        }

        var invalidCharacters = pattern
            .Where(static c => Array.IndexOf(InvalidSearchPatternCharacters, c) >= 0)
            .Distinct()
            .ToArray();
        if (invalidCharacters.Length > 0)
        {
            var joined = string.Join("', '", invalidCharacters.Select(static c => c.ToString()));
            var message = invalidCharacters.Length == 1
                ? $"Search pattern contains an invalid character: '{joined}'."
                : $"Search pattern contains invalid characters: '{joined}'.";
            validationError = CreateValidationError(
                folderPath,
                "invalid_search_pattern",
                message,
                "Remove the invalid characters or replace them with '*' or '?' wildcards.");
            return false;
        }

        validationError = null;
        return true;
    }


    private ImportError CreateValidationError(string filePath, string code, string message, string? suggestion)
        => new(filePath, code, message, suggestion, null, _clock.UtcNow);


    private static NormalizedImportOptions NormalizeOptions(ImportOptions? options)
    {
        var maxFileSize = options?.MaxFileSizeBytes;
        if (maxFileSize.HasValue && maxFileSize.Value <= 0)
        {
            maxFileSize = null;
        }

        var maxDegree = options?.MaxDegreeOfParallelism ?? 1;
        if (maxDegree <= 0)
        {
            maxDegree = 1;
        }

        var bufferSize = options?.BufferSize ?? 64 * 1024;
        if (bufferSize < 4096)
        {
            bufferSize = 4096;
        }

        var searchPattern = string.IsNullOrWhiteSpace(options?.SearchPattern)
            ? "*"
            : options!.SearchPattern!.Trim();
        if (string.IsNullOrEmpty(searchPattern))
        {
            searchPattern = "*";
        }
        var recursive = options?.Recursive ?? true;
        var defaultAuthor = (options?.DefaultAuthor ?? string.Empty).Trim();
        var keepMetadata = options?.KeepFileSystemMetadata ?? true;
        var setReadOnly = options?.SetReadOnly ?? false;

        return new NormalizedImportOptions(
            searchPattern,
            recursive,
            defaultAuthor,
            keepMetadata,
            setReadOnly,
            maxFileSize,
            maxDegree,
            bufferSize);
    }

    private static NormalizedImportOptions NormalizeOptions(ImportFolderRequest request)
    {
        return NormalizeOptions(new ImportOptions
        {
            MaxFileSizeBytes = request.MaxFileSizeBytes,
            MaxDegreeOfParallelism = request.MaxDegreeOfParallelism,
            DefaultAuthor = request.DefaultAuthor,
            KeepFileSystemMetadata = request.KeepFsMetadata,
            SetReadOnly = request.SetReadOnly,
            SearchPattern = request.SearchPattern,
            Recursive = request.Recursive,
        });
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

    private async Task<bool> FileAlreadyExistsAsync(string contentHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return false;
        }

        return await _mediator.Send(new FileHashExistsQuery(contentHash), cancellationToken).ConfigureAwait(false);
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

    private sealed record CreateFileImport(CreateFileRequest Request, string ContentHash);

    private sealed record FileContentReadResult(byte[] Content, string Hash);

    private sealed class FileTooLargeException : Exception
    {
        public FileTooLargeException(string filePath, long actualSizeBytes, long maxAllowedBytes)
            : base($"File '{filePath}' exceeds the configured maximum size.")
        {
            FilePath = filePath;
            ActualSizeBytes = actualSizeBytes;
            MaxAllowedBytes = maxAllowedBytes;
        }

        public string FilePath { get; }

        public long ActualSizeBytes { get; }

        public long MaxAllowedBytes { get; }
    }

    private sealed record class NormalizedImportOptions(
        string SearchPattern,
        bool Recursive,
        string DefaultAuthor,
        bool KeepFileSystemMetadata,
        bool SetReadOnly,
        long? MaxFileSizeBytes,
        int MaxDegreeOfParallelism,
        int BufferSize);
}
