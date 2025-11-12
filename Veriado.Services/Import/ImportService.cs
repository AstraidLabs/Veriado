using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Veriado.Mapping.AC;
using Veriado.Services.Import.Ingestion;
using Veriado.Services.Import.Internal;
using Veriado.Services.Maintenance;
using Veriado.Contracts.Import;
using Veriado.Appl.Abstractions;
using Veriado.Appl.UseCases.Files.CheckFileHash;
using Veriado.Application.Import;
using Veriado.Infrastructure.Common;
using Veriado.Infrastructure.Lifecycle;
using ApplicationImportOptions = Veriado.Application.Import.ImportOptions;
using StreamingImportOptions = Veriado.Services.Import.Ingestion.ImportOptions;
using ContractsImportOptions = Veriado.Contracts.Import.ImportOptions;

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

    private static readonly byte[] MinimalContent = new byte[] { 0x00 };

    private readonly IMediator _mediator;
    private readonly WriteMappingPipeline _mappingPipeline;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;
    private readonly ILogger<ImportService> _logger;
    private readonly IMaintenanceService _maintenanceService;
    private readonly IAppLifecycleService _lifecycleService;
    private readonly IFileStorage _fileStorage;
    private readonly IFileImportWriter _importWriter;
    private readonly SemaphoreSlim _fulltextRepairSemaphore = new(1, 1);
    private readonly SemaphoreSlim _importSemaphore = new(1, 1);
    private bool _fulltextRepairAttempted;
    private bool _fulltextRepairSucceeded;

    public ImportService(
        IMediator mediator,
        WriteMappingPipeline mappingPipeline,
        IClock clock,
        IRequestContext requestContext,
        ILogger<ImportService> logger,
        IMaintenanceService maintenanceService,
        IAppLifecycleService lifecycleService,
        IFileStorage fileStorage,
        IFileImportWriter importWriter)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _mappingPipeline = mappingPipeline ?? throw new ArgumentNullException(nameof(mappingPipeline));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _requestContext = requestContext ?? throw new ArgumentNullException(nameof(requestContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maintenanceService = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));
        _lifecycleService = lifecycleService ?? throw new ArgumentNullException(nameof(lifecycleService));
        _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
        _importWriter = importWriter ?? throw new ArgumentNullException(nameof(importWriter));
    }

    /// <inheritdoc />
    [Obsolete("Use the streaming import APIs.")]
    public async Task<ApiResponse<Guid>> ImportFileAsync(CreateFileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = NormalizeRequest(request);
        var descriptor = string.IsNullOrWhiteSpace(normalized.Name) ? normalized.Extension : normalized.Name;
        var contentHash = ComputeContentHash(normalized.Content);

        await using var import = new CreateFileImport(
            normalized,
            contentHash,
            normalized.Content.LongLength,
            new MemoryStream(normalized.Content, writable: false));

        return await ImportFileInternalAsync(import, descriptor, cancellationToken)
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
        ContractsImportOptions? options,
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
        bool enumerationIssuesEncountered,
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
            if (enumerationIssuesEncountered && errors.Count > 0)
            {
                return ImportBatchStatus.PartialSuccess;
            }

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
        _logger.LogInformation(
            "Starting import for {FolderPath} with options {@Options}",
            folderPath,
            new
            {
                options.SearchPattern,
                options.Recursive,
                options.DefaultAuthor,
                options.KeepFileSystemMetadata,
                options.SetReadOnly,
                options.MaxFileSizeBytes,
                options.MaxDegreeOfParallelism,
                options.BufferSize,
            });

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

        var searchPatternResult = options.SearchPatternResult;
        if (!searchPatternResult.IsValid)
        {
            if (options.SearchPatternSanitized && !string.IsNullOrWhiteSpace(options.SearchPatternWarningMessage))
            {
                var originalDisplay = string.IsNullOrWhiteSpace(options.OriginalSearchPattern)
                    ? "(empty)"
                    : options.OriginalSearchPattern;
                _logger.LogWarning(
                    "Search pattern {OriginalPattern} for folder {FolderPath} was invalid. Falling back to {FallbackPattern}.",
                    originalDisplay,
                    folderPath,
                    options.SearchPattern);
            }

            var errorDetails = searchPatternResult.ValidationError ??
                new SearchPatternValidationError(
                    "Search pattern is invalid.",
                    "Provide a value such as '*' or '*.txt'.");
            var validationError = CreateValidationError(
                folderPath,
                "invalid_search_pattern",
                errorDetails.Message,
                errorDetails.Suggestion);
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

        var enumerationResult = EnumerateFilesWithDiagnostics(folderPath, options);
        if (enumerationResult.FatalError is not null)
        {
            var fatalAggregate = new ImportAggregateResult(
                ImportBatchStatus.FatalError,
                0,
                0,
                0,
                0,
                0,
                new[] { enumerationResult.FatalError });

            yield return ImportProgressEvent.BatchStarted(0, enumerationResult.FatalError.Timestamp);
            yield return ImportProgressEvent.ErrorOccurred(enumerationResult.FatalError, 0, 0, 0, 0, 0, enumerationResult.FatalError.Timestamp);
            yield return ImportProgressEvent.BatchCompleted(fatalAggregate, _clock.UtcNow);
            yield break;
        }

        var files = enumerationResult.Files;
        var enumerationErrors = enumerationResult.Errors;

        if (enumerationErrors.Count > 0)
        {
            _logger.LogWarning(
                "Encountered {ErrorCount} issues while enumerating files in {FolderPath}.",
                enumerationErrors.Count,
                folderPath);
        }

        var total = files.Length;
        var batchStart = _clock.UtcNow;

        _logger.LogInformation(
            "Discovered {Total} candidate files for import in {FolderPath}.",
            total,
            folderPath);

        yield return ImportProgressEvent.BatchStarted(total, batchStart);

        if (enumerationErrors.Count > 0)
        {
            foreach (var enumerationError in enumerationErrors)
            {
                yield return ImportProgressEvent.ErrorOccurred(
                    enumerationError,
                    0,
                    total,
                    0,
                    0,
                    0,
                    enumerationError.Timestamp);
            }
        }

        if (options.SearchPatternSanitized && !string.IsNullOrWhiteSpace(options.SearchPatternWarningMessage))
        {
            var originalDisplay = string.IsNullOrWhiteSpace(options.OriginalSearchPattern)
                ? "(empty)"
                : options.OriginalSearchPattern;
            _logger.LogWarning(
                "Search pattern {OriginalPattern} for folder {FolderPath} was invalid. Falling back to {FallbackPattern}.",
                originalDisplay,
                folderPath,
                options.SearchPattern);

            yield return ImportProgressEvent.Progress(
                processed: 0,
                total: total,
                succeeded: 0,
                failed: 0,
                skipped: 0,
                message: options.SearchPatternWarningMessage,
                timestamp: _clock.UtcNow);
        }

        if (total == 0)
        {
            _logger.LogInformation(
                "No files matched pattern {SearchPattern} in {FolderPath}.",
                options.SearchPattern,
                folderPath);
            yield return ImportProgressEvent.BatchCompleted(ImportAggregateResult.EmptySuccess, _clock.UtcNow);
            yield break;
        }

        var channelCapacity = Math.Clamp(options.MaxDegreeOfParallelism * 3, 6, 128);
        var channel = Channel.CreateBounded<ImportProgressEvent>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        var progressMonitor = new ProgressChannelMonitor(channelCapacity, _logger);

        ValueTask PublishAsync(ImportProgressEvent progress, CancellationToken token) =>
            WriteProgressAsync(channel.Writer, progress, token, progressMonitor);

        var errors = new ConcurrentBag<ImportError>();
        foreach (var enumerationError in enumerationErrors)
        {
            errors.Add(enumerationError);
        }
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var fatalEncountered = false;
        var cancellationEncountered = false;

        var pauseContext = await PauseLifecycleForImportAsync(options.MaxDegreeOfParallelism, cancellationToken)
            .ConfigureAwait(false);
        await using var lifecycleScope = pauseContext.Scope;

        if (pauseContext.PauseDuration is TimeSpan pauseDuration)
        {
            _logger.LogInformation(
                "Background services paused in {Duration}.", pauseDuration);
        }

        if (pauseContext.FailureCount > 0)
        {
            _logger.LogWarning(
                "Lifecycle pause failures encountered before import start: {Failures} attempts.",
                pauseContext.FailureCount);
        }

        if (pauseContext.WarningMessage is not null)
        {
            await PublishAsync(
                ImportProgressEvent.Progress(
                    processed: 0,
                    total: total,
                    succeeded: 0,
                    failed: 0,
                    skipped: 0,
                    message: pauseContext.WarningMessage,
                    timestamp: _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }

        var effectiveParallelism = pauseContext.EffectiveParallelism;

        using var writerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var writerToken = writerCts.Token;

        var writerTask = Task.Run(async () =>
        {
            try
            {
                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = writerToken,
                    MaxDegreeOfParallelism = effectiveParallelism,
                };

                await Parallel.ForEachAsync(files, parallelOptions, async (filePath, token) =>
                {
                    token.ThrowIfCancellationRequested();

                    await PublishAsync(
                        ImportProgressEvent.FileStarted(filePath, Volatile.Read(ref processed), total, _clock.UtcNow),
                        token).ConfigureAwait(false);

                    int successCountSnapshot;
                    int failedCountSnapshot;
                    int skippedSnapshot;

                    try
                    {
                        await using var createRequest = await CreateRequestFromFileAsync(filePath, options, token)
                            .ConfigureAwait(false);

                        if (await FileAlreadyExistsAsync(createRequest.ContentHash, token).ConfigureAwait(false))
                        {
                            _logger.LogInformation(
                                "Skipping file {FilePath} because identical content already exists (SHA256 {Hash}).",
                                filePath,
                                createRequest.ContentHash);

                            var completedSkip = Interlocked.Increment(ref processed);
                            skippedSnapshot = Interlocked.Increment(ref skipped);
                            successCountSnapshot = Volatile.Read(ref succeeded);
                            failedCountSnapshot = Volatile.Read(ref failed);
                            var skipMessage = $"Skipped '{filePath}' because identical content already exists.";

                            await PublishAsync(
                                ImportProgressEvent.FileCompleted(
                                    filePath,
                                    completedSkip,
                                    total,
                                    successCountSnapshot,
                                    skippedSnapshot,
                                    skipMessage,
                                    _clock.UtcNow),
                                token).ConfigureAwait(false);

                            await PublishAsync(
                                ImportProgressEvent.Progress(
                                    completedSkip,
                                    total,
                                    successCountSnapshot,
                                    failedCountSnapshot,
                                    skippedSnapshot,
                                    skipMessage,
                                    _clock.UtcNow),
                                token).ConfigureAwait(false);

                            return;
                        }

                        var response = await ImportFileInternalAsync(createRequest, filePath, token).ConfigureAwait(false);

                        if (!response.IsSuccess
                            && ShouldAttemptFulltextRepair(response.Errors)
                            && await TryRepairFulltextIndexAsync(token).ConfigureAwait(false))
                        {
                            _logger.LogInformation(
                                "Retrying import for {FilePath} after repairing the full-text index.",
                                filePath);
                            response = await ImportFileInternalAsync(createRequest, filePath, token)
                                .ConfigureAwait(false);
                        }

                        if (response.IsSuccess)
                        {
                            var completed = Interlocked.Increment(ref processed);
                            var success = Interlocked.Increment(ref succeeded);
                            skippedSnapshot = Volatile.Read(ref skipped);

                            await PublishAsync(
                                ImportProgressEvent.FileCompleted(
                                    filePath,
                                    completed,
                                    total,
                                    success,
                                    skippedSnapshot,
                                    timestamp: _clock.UtcNow),
                                token).ConfigureAwait(false);

                            await PublishAsync(
                                ImportProgressEvent.Progress(
                                    completed,
                                    total,
                                    success,
                                    Volatile.Read(ref failed),
                                    skippedSnapshot,
                                    timestamp: _clock.UtcNow),
                                token).ConfigureAwait(false);

                            return;
                        }

                        if (IsDuplicateConflict(response.Errors))
                        {
                            _logger.LogInformation(
                                "Skipping file {FilePath} because identical content already exists (detected after write conflict).",
                                filePath);

                            var completedSkip = Interlocked.Increment(ref processed);
                            skippedSnapshot = Interlocked.Increment(ref skipped);
                            successCountSnapshot = Volatile.Read(ref succeeded);
                            failedCountSnapshot = Volatile.Read(ref failed);
                            var skipMessage = $"Skipped '{filePath}' because identical content already exists.";

                            await PublishAsync(
                                ImportProgressEvent.FileCompleted(
                                    filePath,
                                    completedSkip,
                                    total,
                                    successCountSnapshot,
                                    skippedSnapshot,
                                    skipMessage,
                                    _clock.UtcNow),
                                token).ConfigureAwait(false);

                            await PublishAsync(
                                ImportProgressEvent.Progress(
                                    completedSkip,
                                    total,
                                    successCountSnapshot,
                                    failedCountSnapshot,
                                    skippedSnapshot,
                                    skipMessage,
                                    _clock.UtcNow),
                                token).ConfigureAwait(false);

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
                        skippedSnapshot = Volatile.Read(ref skipped);

                        await PublishAsync(
                            ImportProgressEvent.ErrorOccurred(
                                primaryError,
                                completedFailure,
                                total,
                                successCountSnapshot,
                                failedCountSnapshot,
                                skippedSnapshot,
                                _clock.UtcNow),
                            token).ConfigureAwait(false);

                        await PublishAsync(
                            ImportProgressEvent.Progress(
                                completedFailure,
                                total,
                                successCountSnapshot,
                                failedCountSnapshot,
                                skippedSnapshot,
                                timestamp: _clock.UtcNow),
                            token).ConfigureAwait(false);
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
                        skippedSnapshot = Volatile.Read(ref skipped);

                        await PublishAsync(
                            ImportProgressEvent.ErrorOccurred(
                                error,
                                completedFailure,
                                total,
                                successCountSnapshot,
                                failedCountSnapshot,
                                skippedSnapshot,
                                _clock.UtcNow),
                            token).ConfigureAwait(false);

                        await PublishAsync(
                            ImportProgressEvent.Progress(
                                completedFailure,
                                total,
                                successCountSnapshot,
                                failedCountSnapshot,
                                skippedSnapshot,
                                timestamp: _clock.UtcNow),
                            token).ConfigureAwait(false);
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
                        skippedSnapshot = Volatile.Read(ref skipped);

                        await PublishAsync(
                            ImportProgressEvent.ErrorOccurred(
                                error,
                                completedFailure,
                                total,
                                successCountSnapshot,
                                failedCountSnapshot,
                                skippedSnapshot,
                                _clock.UtcNow),
                            token).ConfigureAwait(false);

                        await PublishAsync(
                            ImportProgressEvent.Progress(
                                completedFailure,
                                total,
                                successCountSnapshot,
                                failedCountSnapshot,
                                skippedSnapshot,
                                timestamp: _clock.UtcNow),
                            token).ConfigureAwait(false);
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

                await PublishAsync(
                    ImportProgressEvent.ErrorOccurred(
                        error,
                        Volatile.Read(ref processed),
                        total,
                        Volatile.Read(ref succeeded),
                        Volatile.Read(ref failed),
                        Volatile.Read(ref skipped),
                        _clock.UtcNow),
                    writerToken).ConfigureAwait(false);
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

                await PublishAsync(
                    ImportProgressEvent.ErrorOccurred(
                        error,
                        Volatile.Read(ref processed),
                        total,
                        Volatile.Read(ref succeeded),
                        Volatile.Read(ref failed),
                        Volatile.Read(ref skipped),
                        _clock.UtcNow),
                    writerToken).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, writerToken);

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var progress))
                {
                    progressMonitor.AfterRead();
                    yield return progress;
                }
            }
        }
        finally
        {
            try
            {
                await writerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (writerToken.IsCancellationRequested)
            {
                cancellationEncountered = true;
            }
        }

        var processedFinal = Volatile.Read(ref processed);
        var succeededFinal = Volatile.Read(ref succeeded);
        var failedFinal = Volatile.Read(ref failed);
        var skippedFinal = Volatile.Read(ref skipped);
        var errorArray = errors.ToArray();
        var enumerationIssuesEncountered = enumerationErrors.Count > 0;
        var status = DetermineStatus(
            total,
            processedFinal,
            succeededFinal,
            failedFinal,
            skippedFinal,
            fatalEncountered,
            cancellationEncountered,
            enumerationIssuesEncountered,
            errorArray);
        var aggregate = new ImportAggregateResult(status, total, processedFinal, succeededFinal, failedFinal, skippedFinal, errorArray);

        _logger.LogInformation(
            "Import for {FolderPath} completed with status {Status}. Processed {Processed}/{Total} files (Succeeded: {Succeeded}, Failed: {Failed}, Skipped: {Skipped}). Errors: {ErrorCount}.",
            folderPath,
            aggregate.Status,
            aggregate.Processed,
            aggregate.Total,
            aggregate.Succeeded,
            aggregate.Failed,
            aggregate.Skipped,
            aggregate.Errors.Count);

        if (pauseContext.UnpausedSinceUtc is DateTimeOffset unpausedSince)
        {
            var duration = _clock.UtcNow - unpausedSince;
            _logger.LogWarning(
                "Import completed without lifecycle pause. Duration without pause: {Duration}.", duration);
        }

        progressMonitor.Report(_clock.UtcNow - batchStart);

        yield return ImportProgressEvent.BatchCompleted(aggregate, _clock.UtcNow);
    }

    private FileEnumerationResult EnumerateFilesWithDiagnostics(string folderPath, NormalizedImportOptions options)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var visited = new HashSet<string>(comparer);
        var stack = new Stack<string>();
        var files = new List<string>();
        var errors = new List<ImportError>();
        var rootFullPath = NormalizePathSafe(folderPath);
        stack.Push(rootFullPath);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            var isRoot = comparer.Equals(current, rootFullPath);

            if (!TryEnumerateFiles(current, options, isRoot, errors, out var currentFiles, out var fatalError))
            {
                if (fatalError is not null)
                {
                    return new FileEnumerationResult(Array.Empty<string>(), errors.ToArray(), fatalError);
                }

                continue;
            }

            files.AddRange(currentFiles);

            if (!options.Recursive)
            {
                continue;
            }

            if (!TryEnumerateDirectories(current, isRoot, errors, out var subdirectories, out fatalError))
            {
                if (fatalError is not null)
                {
                    return new FileEnumerationResult(files.ToArray(), errors.ToArray(), fatalError);
                }

                continue;
            }

            foreach (var directory in subdirectories)
            {
                if (ShouldSkipDirectory(directory))
                {
                    continue;
                }

                stack.Push(NormalizePathSafe(directory));
            }
        }

        return new FileEnumerationResult(files.ToArray(), errors.ToArray(), null);
    }

    private bool TryEnumerateFiles(
        string directory,
        NormalizedImportOptions options,
        bool isRoot,
        List<ImportError> errors,
        out string[] files,
        out ImportError? fatalError)
    {
        try
        {
            files = Directory.EnumerateFiles(directory, options.SearchPattern, SearchOption.TopDirectoryOnly).ToArray();
            fatalError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
        {
            var error = CreateEnumerationError(directory, options.SearchPattern, ex, isDirectoryEnumeration: false);
            errors.Add(error);
            var level = isRoot ? LogLevel.Error : LogLevel.Warning;
            _logger.Log(level, ex, "Failed to enumerate files in {Directory} using pattern {Pattern}.", directory, options.SearchPattern);

            files = Array.Empty<string>();
            fatalError = isRoot ? error : null;
            return false;
        }
    }

    private bool TryEnumerateDirectories(
        string directory,
        bool isRoot,
        List<ImportError> errors,
        out string[] subdirectories,
        out ImportError? fatalError)
    {
        try
        {
            subdirectories = Directory.EnumerateDirectories(directory).ToArray();
            fatalError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
        {
            var error = CreateEnumerationError(directory, pattern: null, ex, isDirectoryEnumeration: true);
            errors.Add(error);
            var level = isRoot ? LogLevel.Error : LogLevel.Warning;
            _logger.Log(level, ex, "Failed to enumerate subdirectories in {Directory}.", directory);

            subdirectories = Array.Empty<string>();
            fatalError = isRoot ? error : null;
            return false;
        }
    }

    private ImportError CreateEnumerationError(string scope, string? pattern, Exception exception, bool isDirectoryEnumeration)
    {
        var code = isDirectoryEnumeration ? "directory_enumeration_failed" : "enumeration_failed";
        var message = isDirectoryEnumeration
            ? $"Failed to enumerate subdirectories in '{scope}'. {exception.Message}"
            : $"Failed to enumerate files in '{scope}' using pattern '{pattern}'. {exception.Message}";
        var suggestion = "Verify that the application has access to the folder and retry.";
        return new ImportError(scope, code, message, suggestion, exception.ToString(), _clock.UtcNow);
    }

    private static string NormalizePathSafe(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    private static bool ShouldSkipDirectory(string directory)
    {
        try
        {
            var attributes = File.GetAttributes(directory);
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<ApiResponse<Guid>> ImportFileInternalAsync(
        CreateFileImport import,
        string? descriptor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(import);
        var request = import.Request;

        var mapping = await _mappingPipeline.MapCreateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!mapping.IsSuccess)
        {
            LogApiErrors(descriptor, mapping.Errors);
            return ApiResponse<Guid>.Failure(mapping.Errors);
        }

        var mapped = mapping.Data!;
        var command = mapped.Command;
        var fileId = Guid.NewGuid();

        using var scope = BeginScope();

        if (import.ContentStream.CanSeek)
        {
            import.ContentStream.Seek(0, SeekOrigin.Begin);
        }

        var storageResult = await _fileStorage.SaveAsync(import.ContentStream, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(storageResult.Hash.Value, import.ContentHash, StringComparison.Ordinal))
        {
            var mismatch = new ApiError(
                "content_hash_mismatch",
                "File content changed while it was being read. Please retry the import.",
                descriptor);
            LogApiError(descriptor, mismatch);
            return ApiResponse<Guid>.Failure(mismatch);
        }

        var importItem = CreateImportItem(fileId, request, mapped, storageResult, import.ContentHash);
        var importResult = await InvokeImportAsync(importItem, cancellationToken).ConfigureAwait(false);

        if (importResult.Imported + importResult.Updated > 0)
        {
            var followUpCommands = mapped.BuildFollowUpCommands(fileId).ToArray();
            foreach (var followUp in followUpCommands)
            {
                var followUpResult = await _mediator.Send(followUp, cancellationToken).ConfigureAwait(false);
                if (!followUpResult.IsSuccess)
                {
                    var error = ConvertAppError(followUpResult.Error);
                    LogApiError(descriptor, error);
                    return ApiResponse<Guid>.Failure(error);
                }
            }

            return ApiResponse<Guid>.Success(fileId);
        }

        var skipError = new ApiError(
            "import_skipped",
            "File was skipped because a newer version already exists.",
            descriptor);
        LogApiError(descriptor, skipError);
        return ApiResponse<Guid>.Failure(skipError);
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
        if (maxFileSizeBytes.HasValue && contentResult.Length > maxFileSizeBytes.Value)
        {
            await contentResult.Stream.DisposeAsync().ConfigureAwait(false);
            throw new FileTooLargeException(filePath, contentResult.Length, maxFileSizeBytes.Value);
        }

        try
        {
            var request = NormalizeRequest(new CreateFileRequest
            {
                Name = name,
                Extension = extension,
                Mime = MimeMap.GetMimeType(extension),
                Author = author,
                Content = MinimalContent,
                MaxContentLength = maxFileSizeBytes.HasValue && maxFileSizeBytes.Value <= int.MaxValue
                    ? (int?)maxFileSizeBytes.Value
                    : null,
                SystemMetadata = systemMetadata,
                IsReadOnly = isReadOnly,
            });

            return new CreateFileImport(request, contentResult.Hash, contentResult.Length, contentResult.Stream);
        }
        catch
        {
            await contentResult.Stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<FileContentReadResult> ReadFileContentAsync(
        string filePath,
        NormalizedImportOptions options,
        CancellationToken cancellationToken)
    {
        var streamingOptions = CreateStreamingOptions(options);
        var stream = await FileOpener
            .OpenForReadWithRetryAsync(filePath, streamingOptions, _logger, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (options.MaxFileSizeBytes.HasValue && stream.CanSeek && stream.Length > options.MaxFileSizeBytes.Value)
            {
                throw new FileTooLargeException(filePath, stream.Length, options.MaxFileSizeBytes.Value);
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(streamingOptions.BufferSize);
            long total = 0;

            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    hash.AppendData(buffer, 0, read);
                    total += read;

                    if (options.MaxFileSizeBytes.HasValue && total > options.MaxFileSizeBytes.Value)
                    {
                        throw new FileTooLargeException(filePath, total, options.MaxFileSizeBytes.Value);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }

            var hashText = Convert.ToHexString(hash.GetHashAndReset());
            _logger.LogDebug("Read {Length} bytes from {FilePath} (SHA256 {Hash}).", total, filePath, hashText);

            return new FileContentReadResult(stream, total, hashText);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static StreamingImportOptions CreateStreamingOptions(NormalizedImportOptions options)
        => new()
        {
            BufferSize = options.BufferSize,
            MaxRetryCount = options.FileOpenRetryCount,
            RetryBaseDelay = options.FileOpenRetryBaseDelay,
            MaxRetryDelay = options.FileOpenMaxRetryDelay,
            SharePolicy = options.SharePolicy,
        };

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

    private async Task<bool> TryRepairFulltextIndexAsync(CancellationToken cancellationToken)
    {
        await _fulltextRepairSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_fulltextRepairSucceeded)
            {
                return false;
            }

            if (_fulltextRepairAttempted)
            {
                return _fulltextRepairSucceeded;
            }

            _fulltextRepairAttempted = true;
            _logger.LogWarning("Detected SQLite corruption during import; attempting full-text repair.");

            var repairResult = await _maintenanceService
                .VerifyAndRepairAsync(forceRepair: true, cancellationToken)
                .ConfigureAwait(false);

            if (repairResult.IsSuccess)
            {
                _fulltextRepairSucceeded = true;
                _logger.LogInformation(
                    "Full-text repair completed while recovering from import failure ({RepairedCount} entries).",
                    repairResult.Value);
                return true;
            }

            var error = repairResult.Error;
            string? details = null;
            if (error.Details is { Count: > 0 } detailsCollection)
            {
                var flattened = detailsCollection
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
                if (flattened.Length > 0)
                {
                    details = string.Join("; ", flattened);
                }
            }

            if (string.IsNullOrWhiteSpace(details))
            {
                _logger.LogError(
                    "Full-text repair failed while recovering from import failure: {Message}",
                    error.Message);
            }
            else
            {
                _logger.LogError(
                    "Full-text repair failed while recovering from import failure: {Message} ({Details})",
                    error.Message,
                    details);
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while attempting full-text repair during import recovery.");
            return false;
        }
        finally
        {
            _fulltextRepairSemaphore.Release();
        }
    }

    private static bool ShouldAttemptFulltextRepair(IReadOnlyList<ApiError> errors)
    {
        if (errors is not { Count: > 0 })
        {
            return false;
        }

        foreach (var error in errors)
        {
            if (!string.Equals(error.Code, "database_error", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ContainsCorruptionIndicator(error.Message))
            {
                return true;
            }

            if (error.Details is { Count: > 0 })
            {
                foreach (var detail in error.Details.Values.SelectMany(static values => values))
                {
                    if (ContainsCorruptionIndicator(detail))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool ContainsCorruptionIndicator(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.ToLowerInvariant();
        if (normalized.Contains("malformed", StringComparison.Ordinal))
        {
            return true;
        }

        if (normalized.Contains("sqlite error 11", StringComparison.Ordinal)
            || normalized.Contains("sqlite_error 11", StringComparison.Ordinal)
            || normalized.Contains("sqlite_corrupt", StringComparison.Ordinal)
            || normalized.Contains("sqlite-corrupt", StringComparison.Ordinal))
        {
            return true;
        }

        if (normalized.Contains("no such table", StringComparison.Ordinal)
            && (normalized.Contains("search_document_fts", StringComparison.Ordinal)
                || normalized.Contains("fts", StringComparison.Ordinal)))
        {
            return true;
        }

        if (normalized.Contains("fts5", StringComparison.Ordinal)
            && (normalized.Contains("error", StringComparison.Ordinal)
                || normalized.Contains("corrupt", StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
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


    private ImportError CreateValidationError(string filePath, string code, string message, string? suggestion)
        => new(filePath, code, message, suggestion, null, _clock.UtcNow);


    private static NormalizedImportOptions NormalizeOptions(ContractsImportOptions? options)
    {
        var rawSearchPattern = options?.SearchPattern;
        var searchPatternResult = rawSearchPattern is null
            ? new SearchPatternResult("*", true, Array.Empty<string>(), null)
            : NormalizeAndValidatePattern(rawSearchPattern);
        var normalizedSearchPattern = searchPatternResult.Normalized;

        string? originalPattern = null;
        var searchPatternSanitized = false;
        string? warningMessage = null;

        if (rawSearchPattern is not null)
        {
            var trimmed = rawSearchPattern.Trim();
            warningMessage = searchPatternResult.Warnings.FirstOrDefault();

            if (trimmed.Length == 0)
            {
                originalPattern = rawSearchPattern;
                searchPatternSanitized = true;
            }
            else
            {
                originalPattern = trimmed;
                searchPatternSanitized = !string.Equals(normalizedSearchPattern, trimmed, StringComparison.Ordinal);
            }
        }

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

        var recursive = options?.Recursive ?? true;
        var defaultAuthor = (options?.DefaultAuthor ?? string.Empty).Trim();
        var keepMetadata = options?.KeepFileSystemMetadata ?? true;
        var setReadOnly = options?.SetReadOnly ?? false;

        var retryCount = options?.FileOpenRetryCount ?? 5;
        if (retryCount < 0)
        {
            retryCount = 0;
        }

        static TimeSpan NormalizeDelay(int? milliseconds, int fallback)
        {
            var value = milliseconds.GetValueOrDefault(fallback);
            if (value <= 0)
            {
                value = fallback;
            }

            return TimeSpan.FromMilliseconds(value);
        }

        var baseDelay = NormalizeDelay(options?.FileOpenRetryBaseDelayMilliseconds, 200);
        var maxDelay = NormalizeDelay(options?.FileOpenRetryMaxDelayMilliseconds, 2000);
        if (maxDelay < baseDelay)
        {
            maxDelay = baseDelay;
        }

        var sharePolicy = options?.AllowSourceFileDeletion ?? false
            ? FileOpenSharePolicy.ReadWriteDelete
            : FileOpenSharePolicy.ReadWrite;

        return new NormalizedImportOptions(
            normalizedSearchPattern,
            recursive,
            defaultAuthor,
            keepMetadata,
            setReadOnly,
            maxFileSize,
            maxDegree,
            bufferSize,
            originalPattern,
            searchPatternSanitized,
            warningMessage,
            searchPatternResult,
            retryCount,
            baseDelay,
            maxDelay,
            sharePolicy);
    }

    private static NormalizedImportOptions NormalizeOptions(ImportFolderRequest request)
    {
        return NormalizeOptions(new ContractsImportOptions
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

    private static SearchPatternResult NormalizeAndValidatePattern(string raw)
    {
        var trimmed = raw.Trim();
        var warnings = new List<string>();
        var normalized = trimmed;
        var isValid = true;
        SearchPatternValidationError? validationError = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            normalized = "*";
            warnings.Add("Search pattern was empty. Using '*' instead.");
            isValid = false;
            validationError = new SearchPatternValidationError(
                "Search pattern cannot be empty.",
                "Provide a value such as '*' or '*.txt'.");
        }
        else if (trimmed.IndexOfAny(SearchPatternPathSeparators) >= 0)
        {
            normalized = "*";
            warnings.Add($"Search pattern '{trimmed}' cannot contain directory separators. Using '*' instead.");
            isValid = false;
            validationError = new SearchPatternValidationError(
                "Search pattern must not contain directory separators.",
                "Remove '/' or '\\' characters from the search pattern.");
        }
        else
        {
            var invalidCharacters = trimmed
                .Where(static c => Array.IndexOf(InvalidSearchPatternCharacters, c) >= 0)
                .Distinct()
                .ToArray();

            if (invalidCharacters.Length > 0)
            {
                var joined = string.Join("', '", invalidCharacters.Select(static c => c.ToString()));
                var descriptor = invalidCharacters.Length == 1
                    ? $"an invalid character '{joined}'"
                    : $"invalid characters '{joined}'";
                normalized = "*";
                warnings.Add($"Search pattern '{trimmed}' contains {descriptor}. Using '*' instead.");
                isValid = false;
                var message = invalidCharacters.Length == 1
                    ? $"Search pattern contains an invalid character: '{joined}'."
                    : $"Search pattern contains invalid characters: '{joined}'.";
                validationError = new SearchPatternValidationError(
                    message,
                    "Remove the invalid characters or replace them with '*' or '?' wildcards.");
            }
        }

        return new SearchPatternResult(
            normalized,
            isValid,
            warnings.Count == 0 ? Array.Empty<string>() : warnings.ToArray(),
            validationError);
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

    private static string ComputeContentHash(byte[] content)
    {
        if (content is null || content.Length == 0)
        {
            using var emptyHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            return Convert.ToHexString(emptyHash.GetHashAndReset());
        }

        return Convert.ToHexString(SHA256.HashData(content));
    }

    private sealed record CreateFileImport(
        CreateFileRequest Request,
        string ContentHash,
        long ContentLength,
        Stream ContentStream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ContentStream.DisposeAsync();
    }

    private ImportItem CreateImportItem(
        Guid fileId,
        CreateFileRequest request,
        CreateFileMappedRequest mapped,
        StorageResult storageResult,
        string contentHash)
    {
        var systemMetadataDto = request.SystemMetadata;
        var isReadOnly = request.IsReadOnly || mapped.SetReadOnly;

        var fileSystemMetadata = systemMetadataDto is null
            ? new ImportFileSystemMetadata(
                (int)storageResult.Attributes,
                storageResult.OwnerSid,
                storageResult.IsEncrypted,
                storageResult.CreatedUtc.Value,
                storageResult.LastWriteUtc.Value,
                storageResult.LastAccessUtc.Value,
                null,
                null)
            : new ImportFileSystemMetadata(
                systemMetadataDto.Attributes,
                systemMetadataDto.OwnerSid,
                storageResult.IsEncrypted,
                systemMetadataDto.CreatedUtc,
                systemMetadataDto.LastWriteUtc,
                systemMetadataDto.LastAccessUtc,
                systemMetadataDto.HardLinkCount,
                systemMetadataDto.AlternateDataStreamCount);

        var metadata = new ImportMetadata(
            mapped.Command.Author,
            request.Name,
            isReadOnly,
            Version: 1,
            LinkedContentVersion: 1,
            FileSystemId: null,
            FileSystem: fileSystemMetadata,
            Validity: null,
            Search: new ImportSearchMetadata(
                SchemaVersion: 1,
                IsStale: true,
                IndexedUtc: null,
                IndexedTitle: null,
                IndexedContentHash: contentHash,
                AnalyzerVersion: null,
                TokenHash: null),
            FtsPolicy: new ImportFtsPolicy(true, "unicode61", "-_."));

        var createdUtc = systemMetadataDto?.CreatedUtc ?? storageResult.CreatedUtc.Value;
        var modifiedUtc = systemMetadataDto?.LastWriteUtc ?? storageResult.LastWriteUtc.Value;

        return new ImportItem(
            fileId,
            request.Name,
            request.Extension,
            request.Mime,
            storageResult.Size.Value,
            storageResult.Hash.Value,
            storageResult.Provider.ToString(),
            storageResult.Path.Value,
            createdUtc,
            modifiedUtc,
            metadata);
    }

    private async Task<LifecyclePauseContext> PauseLifecycleForImportAsync(int requestedParallelism, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        var attempt = 0;
        var delay = TimeSpan.FromMilliseconds(200);

        while (attempt < maxAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            PauseResult pauseResult;
            try
            {
                pauseResult = await _lifecycleService.PauseAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            if (pauseResult.Status == PauseStatus.Succeeded)
            {
                _logger.LogInformation(
                    "Paused background services before import in {Duration} (attempt {Attempt}).",
                    pauseResult.Duration,
                    attempt);
                return new LifecyclePauseContext(
                    new LifecycleResumeScope(_lifecycleService, _logger),
                    true,
                    requestedParallelism,
                    attempt - 1,
                    null,
                    pauseResult.Duration,
                    null);
            }

            if (pauseResult.Status == PauseStatus.NotSupported)
            {
                var reduced = Math.Max(1, Math.Min(2, requestedParallelism));
                _logger.LogWarning(
                    "Lifecycle pause not supported in state {State}. Import will continue with M-DOP {Parallelism}.",
                    _lifecycleService.State,
                    reduced);
                return new LifecyclePauseContext(
                    NoopAsyncDisposable.Instance,
                    false,
                    reduced,
                    attempt,
                    _clock.UtcNow,
                    null,
                    "Pozastavení služeb není dostupné. Import běží se sníženou zátěží.");
            }

            _logger.LogWarning(
                pauseResult.Exception,
                "Attempt {Attempt} to pause lifecycle failed. Retrying in {Delay}.",
                attempt,
                delay);

            if (attempt >= maxAttempts)
            {
                break;
            }

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1000));
        }

        var fallbackParallelism = Math.Max(1, Math.Min(2, requestedParallelism));
        _logger.LogWarning(
            "Unable to pause lifecycle after {Attempts} attempts. Continuing import with reduced M-DOP {Parallelism}.",
            attempt,
            fallbackParallelism);

        return new LifecyclePauseContext(
            NoopAsyncDisposable.Instance,
            false,
            fallbackParallelism,
            attempt,
            _clock.UtcNow,
            null,
            "Nepodařilo se pozastavit služby. Import pokračuje se sníženým paralelismem.");
    }

    private async Task<ImportResult> InvokeImportAsync(ImportItem item, CancellationToken cancellationToken)
    {
        await _importSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SqliteRetry.ExecuteAsync(
                    () => _importWriter.ImportAsync(new[] { item }, new ApplicationImportOptions(), cancellationToken),
                    (exception, attempt, delay) =>
                    {
                        _logger.LogWarning(
                            exception,
                            "SQLite busy while importing {StoragePath}. Attempt {Attempt}; retrying in {Delay}.",
                            item.StoragePath,
                            attempt,
                            delay);
                        return Task.CompletedTask;
                    },
                    (exception, attempt) =>
                    {
                        _logger.LogError(
                            exception,
                            "Exceeded retry budget ({Attempts}) while importing {StoragePath} due to SQLITE_BUSY.",
                            attempt,
                            item.StoragePath);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _importSemaphore.Release();
        }
    }

    private sealed class ProgressChannelMonitor
    {
        private readonly ILogger _logger;
        private readonly int _capacity;
        private readonly int _warnThreshold;
        private readonly int _throttleThreshold;
        private static readonly TimeSpan ThrottleDelay = TimeSpan.FromMilliseconds(50);
        private int _pending;
        private int _maxObserved;
        private int _warned;

        public ProgressChannelMonitor(int capacity, ILogger logger)
        {
            _capacity = capacity;
            _logger = logger;
            _warnThreshold = Math.Max(4, (int)Math.Round(capacity * 0.75, MidpointRounding.AwayFromZero));
            _throttleThreshold = Math.Max(_warnThreshold, capacity - 1);
        }

        public async ValueTask AfterWriteAsync(CancellationToken cancellationToken)
        {
            var queued = Interlocked.Increment(ref _pending);
            UpdateMax(queued);

            if (queued >= _warnThreshold && Interlocked.Exchange(ref _warned, 1) == 0)
            {
                _logger.LogWarning(
                    "Import progress backlog reached {Queued}/{Capacity} messages.",
                    queued,
                    _capacity);
            }

            if (queued >= _throttleThreshold)
            {
                try
                {
                    await Task.Delay(ThrottleDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
            }
        }

        public void AfterRead()
        {
            var remaining = Interlocked.Decrement(ref _pending);
            if (remaining < _warnThreshold)
            {
                Interlocked.Exchange(ref _warned, 0);
            }
        }

        public void Report(TimeSpan elapsed)
        {
            var max = Volatile.Read(ref _maxObserved);
            if (max > _warnThreshold)
            {
                _logger.LogInformation(
                    "Maximum import progress backlog {Max} messages observed (capacity {Capacity}, duration {Duration}).",
                    max,
                    _capacity,
                    elapsed);
            }
        }

        private void UpdateMax(int queued)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maxObserved);
                if (queued <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxObserved, queued, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed record LifecyclePauseContext(
        IAsyncDisposable Scope,
        bool IsPaused,
        int EffectiveParallelism,
        int FailureCount,
        DateTimeOffset? UnpausedSinceUtc,
        TimeSpan? PauseDuration,
        string? WarningMessage);

    private sealed class LifecycleResumeScope : IAsyncDisposable
    {
        private readonly IAppLifecycleService _lifecycleService;
        private readonly ILogger _logger;
        private bool _disposed;

        public LifecycleResumeScope(IAppLifecycleService lifecycleService, ILogger logger)
        {
            _lifecycleService = lifecycleService;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                await _lifecycleService.ResumeAsync().ConfigureAwait(false);
                _logger.LogInformation("Resumed background services after import.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resume background services after import.");
            }
        }
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static readonly IAsyncDisposable Instance = new NoopAsyncDisposable();

        private NoopAsyncDisposable()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async ValueTask WriteProgressAsync(
        ChannelWriter<ImportProgressEvent> writer,
        ImportProgressEvent progress,
        CancellationToken cancellationToken,
        ProgressChannelMonitor? monitor = null)
    {
        try
        {
            await writer.WriteAsync(progress, cancellationToken).ConfigureAwait(false);
            if (monitor is not null)
            {
                await monitor.AfterWriteAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private sealed record FileEnumerationResult(string[] Files, IReadOnlyList<ImportError> Errors, ImportError? FatalError);

    private sealed record FileContentReadResult(Stream Stream, long Length, string Hash);

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
        int BufferSize,
        string? OriginalSearchPattern,
        bool SearchPatternSanitized,
        string? SearchPatternWarningMessage,
        SearchPatternResult SearchPatternResult,
        int FileOpenRetryCount,
        TimeSpan FileOpenRetryBaseDelay,
        TimeSpan FileOpenMaxRetryDelay,
        FileOpenSharePolicy SharePolicy);

    private readonly record struct SearchPatternResult(
        string Normalized,
        bool IsValid,
        string[] Warnings,
        SearchPatternValidationError? ValidationError);

    private readonly record struct SearchPatternValidationError(string Message, string? Suggestion);
}
