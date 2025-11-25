using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Veriado.Appl.Abstractions;
using Veriado.Application.Abstractions;
using FileSystemSyncService = Veriado.Appl.Abstractions.IFileSystemSyncService;
using Veriado.Domain.FileSystem;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence;
using DomainClock = Veriado.Domain.Primitives.IClock;

namespace Veriado.Infrastructure.FileSystem;

internal sealed class FileSystemHealthCheckWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFilePathResolver _pathResolver;
    private readonly FileSystemHealthCheckOptions _options;
    private readonly IOperationalPauseCoordinator _pauseCoordinator;
    private readonly IApplicationMaintenanceCoordinator _maintenanceCoordinator;
    private readonly ILogger<FileSystemHealthCheckWorker> _logger;
    private readonly FileSystemSyncService _syncService;

    public FileSystemHealthCheckWorker(
        IServiceScopeFactory scopeFactory,
        IFilePathResolver pathResolver,
        IOptions<FileSystemHealthCheckOptions> options,
        IOperationalPauseCoordinator pauseCoordinator,
        IApplicationMaintenanceCoordinator maintenanceCoordinator,
        FileSystemSyncService syncService,
        ILogger<FileSystemHealthCheckWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _pauseCoordinator = pauseCoordinator ?? throw new ArgumentNullException(nameof(pauseCoordinator));
        _maintenanceCoordinator = maintenanceCoordinator
            ?? throw new ArgumentNullException(nameof(maintenanceCoordinator));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = _options.Interval;

        _logger.LogInformation(
            "{WorkerName} started with interval {Interval} and batch size {BatchSize}.",
            nameof(FileSystemHealthCheckWorker),
            interval,
            _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _maintenanceCoordinator.WaitForResumeAsync(stoppingToken).ConfigureAwait(false);
                await _pauseCoordinator.WaitIfPausedAsync(stoppingToken).ConfigureAwait(false);
                await RunHealthCheckIterationAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File system health check iteration failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // shutting down
            }
        }
    }

    private async Task RunHealthCheckIterationAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<DomainClock>();
        var hashCalculator = scope.ServiceProvider.GetRequiredService<IFileHashCalculator>();

        var batchSize = _options.BatchSize;
        var lastId = Guid.Empty;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await dbContext.FileSystems
                .AsTracking()
                .Where(file => file.Id.CompareTo(lastId) > 0)
                .OrderBy(file => file.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                break;
            }

            await CheckBatchAsync(batch, dbContext, clock, hashCalculator, cancellationToken).ConfigureAwait(false);

            lastId = batch[^1].Id;
        }
    }

    private async Task CheckBatchAsync(
        List<FileSystemEntity> batch,
        AppDbContext dbContext,
        DomainClock clock,
        IFileHashCalculator hashCalculator,
        CancellationToken cancellationToken)
    {
        var syncActions = new List<FileSystemSyncAction>();

        if (_options.EnableParallelChecks)
        {
            var results = new ConcurrentBag<FileEvaluationResult>();
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, _options.MaxDegreeOfParallelism),
            };

            await Parallel.ForEachAsync(batch, parallelOptions, async (file, ct) =>
            {
                var result = await EvaluateFileAsync(file, hashCalculator, ct).ConfigureAwait(false);
                results.Add(result);
            }).ConfigureAwait(false);

            var fileMap = batch.ToDictionary(f => f.Id);
            foreach (var result in results.OrderBy(r => r.Id))
            {
                var syncAction = ApplyResult(fileMap[result.Id], result, clock);
                if (syncAction is not null)
                {
                    syncActions.Add(syncAction);
                }
            }
        }
        else
        {
            foreach (var file in batch)
            {
                var result = await EvaluateFileAsync(file, hashCalculator, cancellationToken).ConfigureAwait(false);
                var syncAction = ApplyResult(file, result, clock);
                if (syncAction is not null)
                {
                    syncActions.Add(syncAction);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await DispatchSyncActionsAsync(syncActions, cancellationToken).ConfigureAwait(false);
        dbContext.ChangeTracker.Clear();
    }

    private async Task<FileEvaluationResult> EvaluateFileAsync(
        FileSystemEntity file,
        IFileHashCalculator hashCalculator,
        CancellationToken cancellationToken)
    {
        var fullPath = _pathResolver.GetFullPath(file);
        if (!File.Exists(fullPath))
        {
            return FileEvaluationResult.Missing(file.Id);
        }

        var info = new FileInfo(fullPath);
        var size = ByteSize.From(info.Length);
        var created = UtcTimestamp.From(info.CreationTimeUtc);
        var lastWrite = UtcTimestamp.From(info.LastWriteTimeUtc);
        var lastAccess = UtcTimestamp.From(info.LastAccessTimeUtc);

        var sizeChanged = size != file.Size;
        var timestampsChanged = file.CreatedUtc != created || file.LastWriteUtc != lastWrite || file.LastAccessUtc != lastAccess;

        if (!sizeChanged && !timestampsChanged)
        {
            return FileEvaluationResult.Unchanged(file.Id, size, created, lastWrite, lastAccess, file.Hash);
        }

        var hash = await hashCalculator.ComputeSha256Async(fullPath, cancellationToken).ConfigureAwait(false);
        if (hash == file.Hash)
        {
            return FileEvaluationResult.MetadataChanged(file.Id, size, created, lastWrite, lastAccess, hash);
        }

        return FileEvaluationResult.ContentChanged(file.Id, size, created, lastWrite, lastAccess, hash);
    }

    private static FileSystemSyncAction? ApplyResult(FileSystemEntity file, FileEvaluationResult result, DomainClock clock)
    {
        var wasMissing = file.IsMissing;

        switch (result.Outcome)
        {
            case FileCheckOutcome.Missing:
                file.MarkMissing(clock);
                return new FileSystemSyncAction(FileSystemSyncEvent.Missing, file.Id);
            case FileCheckOutcome.Unchanged:
                file.MarkHealthy();
                if (wasMissing)
                {
                    return new FileSystemSyncAction(FileSystemSyncEvent.Rehydrated, file.Id);
                }

                return null;
            case FileCheckOutcome.MetadataChanged:
                file.UpdateTimestamps(result.Created, result.LastWrite, result.LastAccess, UtcTimestamp.From(clock.UtcNow));
                file.MarkHealthy();
                if (wasMissing)
                {
                    return new FileSystemSyncAction(FileSystemSyncEvent.Rehydrated, file.Id);
                }

                return null;
            case FileCheckOutcome.ContentChanged:
                file.ReplaceContent(file.RelativePath, result.Hash, result.Size, file.Mime, file.IsEncrypted, result.LastWrite);
                file.UpdateTimestamps(result.Created, result.LastWrite, result.LastAccess, UtcTimestamp.From(clock.UtcNow));
                file.MarkContentChanged();
                return new FileSystemSyncAction(FileSystemSyncEvent.ContentChanged, file.Id);
            default:
                throw new InvalidOperationException($"Unsupported file check outcome '{result.Outcome}'.");
        }
    }

    private async Task DispatchSyncActionsAsync(IEnumerable<FileSystemSyncAction> syncActions, CancellationToken cancellationToken)
    {
        foreach (var action in syncActions)
        {
            try
            {
                switch (action.EventType)
                {
                    case FileSystemSyncEvent.Missing:
                        await _syncService.HandleFileMissingAsync(action.FileSystemId, cancellationToken).ConfigureAwait(false);
                        break;
                    case FileSystemSyncEvent.Rehydrated:
                        await _syncService.HandleFileRehydratedAsync(action.FileSystemId, cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case FileSystemSyncEvent.Moved:
                        await _syncService.HandleFileMovedAsync(action.FileSystemId, cancellationToken).ConfigureAwait(false);
                        break;
                    case FileSystemSyncEvent.ContentChanged:
                        await _syncService.HandleFileContentChangedAsync(action.FileSystemId, cancellationToken)
                            .ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to coordinate logical file update for file system id {FileSystemId} during health check.",
                    action.FileSystemId);
            }
        }
    }

    private enum FileCheckOutcome
    {
        Missing,
        Unchanged,
        MetadataChanged,
        ContentChanged,
    }

    private sealed record FileEvaluationResult(
        Guid Id,
        FileCheckOutcome Outcome,
        ByteSize Size,
        UtcTimestamp Created,
        UtcTimestamp LastWrite,
        UtcTimestamp LastAccess,
        FileHash Hash)
    {
        public static FileEvaluationResult Missing(Guid id) =>
            new(id, FileCheckOutcome.Missing, default, default, default, default, default);

        public static FileEvaluationResult Unchanged(
            Guid id,
            ByteSize size,
            UtcTimestamp created,
            UtcTimestamp lastWrite,
            UtcTimestamp lastAccess,
            FileHash hash) => new(id, FileCheckOutcome.Unchanged, size, created, lastWrite, lastAccess, hash);

        public static FileEvaluationResult MetadataChanged(
            Guid id,
            ByteSize size,
            UtcTimestamp created,
            UtcTimestamp lastWrite,
            UtcTimestamp lastAccess,
            FileHash hash) => new(id, FileCheckOutcome.MetadataChanged, size, created, lastWrite, lastAccess, hash);

        public static FileEvaluationResult ContentChanged(
            Guid id,
            ByteSize size,
            UtcTimestamp created,
            UtcTimestamp lastWrite,
            UtcTimestamp lastAccess,
            FileHash hash) => new(id, FileCheckOutcome.ContentChanged, size, created, lastWrite, lastAccess, hash);
    }
}
