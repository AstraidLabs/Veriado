using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using ApplicationFileSystemSyncService = Veriado.Appl.Abstractions.IFileSystemSyncService;
using Veriado.Domain.FileSystem;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence;
using ApplicationClock = Veriado.Appl.Abstractions.IClock;
using DomainClock = Veriado.Domain.Primitives.IClock;
using Veriado.Application.Abstractions;

namespace Veriado.Infrastructure.FileSystem;

internal sealed class FileSystemMonitoringService : IFileSystemMonitoringService, IAsyncDisposable, IDisposable
{
    private readonly IFilePathResolver _pathResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOperationalPauseCoordinator _pauseCoordinator;
    private readonly IApplicationMaintenanceCoordinator _maintenanceCoordinator;
    private readonly ApplicationFileSystemSyncService _syncService;
    private readonly ApplicationClock _clock;
    private readonly ILogger<FileSystemMonitoringService> _logger;
    private readonly Channel<FileSystemEvent> _eventChannel;
    private readonly TimeSpan _debounceWindow = TimeSpan.FromMilliseconds(250);

    private readonly object _syncRoot = new();
    private Task? _processingTask;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _cts;
    private bool _started;

    public FileSystemMonitoringService(
        IFilePathResolver pathResolver,
        IServiceScopeFactory scopeFactory,
        IOperationalPauseCoordinator pauseCoordinator,
        IApplicationMaintenanceCoordinator maintenanceCoordinator,
        ApplicationFileSystemSyncService syncService,
        ApplicationClock clock,
        ILogger<FileSystemMonitoringService> logger)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _pauseCoordinator = pauseCoordinator ?? throw new ArgumentNullException(nameof(pauseCoordinator));
        _maintenanceCoordinator = maintenanceCoordinator
            ?? throw new ArgumentNullException(nameof(maintenanceCoordinator));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _eventChannel = Channel.CreateBounded<FileSystemEvent>(
            new BoundedChannelOptions(1024)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public void Start(CancellationToken externalCancellationToken)
    {
        lock (_syncRoot)
        {
            if (_started)
            {
                _logger.LogDebug("File system monitoring already started.");
                return;
            }

            string root;
            try
            {
                root = _pathResolver.GetStorageRoot();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve storage root for monitoring.");
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
            var token = _cts.Token;

            _watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.DirectoryName |
                               NotifyFilters.Size |
                               NotifyFilters.LastWrite,
            };

            _watcher.Created += OnCreated;
            _watcher.Deleted += OnDeleted;
            _watcher.Renamed += OnRenamed;
            _watcher.Changed += OnChanged;
            _watcher.EnableRaisingEvents = true;

            // žádný Task.Run s tokenem – bìží to normálnì jako background Task
            _processingTask = ProcessEventsAsync(token);

            _started = true;
            _logger.LogInformation("File system monitoring started for root {Root}.", root);
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        Task? processingTask;
        CancellationTokenSource? ctsToCancel;
        FileSystemWatcher? watcherToDispose;

        lock (_syncRoot)
        {
            if (!_started)
            {
                return;
            }

            watcherToDispose = _watcher;
            _watcher = null;

            ctsToCancel = _cts;
            _cts = null;

            processingTask = _processingTask;
            _processingTask = null;

            _started = false;
        }

        if (watcherToDispose is not null)
        {
            watcherToDispose.EnableRaisingEvents = false;
            watcherToDispose.Created -= OnCreated;
            watcherToDispose.Deleted -= OnDeleted;
            watcherToDispose.Renamed -= OnRenamed;
            watcherToDispose.Changed -= OnChanged;
            watcherToDispose.Dispose();
        }

        // ukonèíme kanál – ètení skonèí ChannelClosedException
        _eventChannel.Writer.TryComplete();

        // zrušíme interní token – kdo èeká na delay / pause / maintenance, skonèí OCE
        ctsToCancel?.Cancel();

        if (processingTask is not null)
        {
            try
            {
                await processingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("File system monitoring processing task cancelled during shutdown.");
            }
            catch (ChannelClosedException)
            {
                _logger.LogDebug("File system monitoring channel closed during shutdown.");
            }
        }

        ctsToCancel?.Dispose();
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        var token = _cts?.Token ?? CancellationToken.None;
        _ = EnqueueWatcherEventAsync(FileSystemEventType.Created, e.FullPath, null, token);
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        var token = _cts?.Token ?? CancellationToken.None;
        _ = EnqueueWatcherEventAsync(FileSystemEventType.Deleted, e.FullPath, null, token);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        var token = _cts?.Token ?? CancellationToken.None;
        _ = EnqueueWatcherEventAsync(FileSystemEventType.Renamed, e.FullPath, e.OldFullPath, token);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        var token = _cts?.Token ?? CancellationToken.None;
        _ = EnqueueWatcherEventAsync(FileSystemEventType.Changed, e.FullPath, null, token);
    }

    private async Task EnqueueWatcherEventAsync(
        FileSystemEventType eventType,
        string fullPath,
        string? oldFullPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await HandleWatcherEventAsync(eventType, fullPath, oldFullPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("File system monitoring operation canceled while enqueueing watcher event.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while enqueueing watcher event.");
        }
    }

    private async Task HandleWatcherEventAsync(
        FileSystemEventType eventType,
        string fullPath,
        string? oldFullPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await QueueEventAsync(eventType, fullPath, oldFullPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("File system monitoring operation canceled while queuing an event.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while queuing file system monitoring event.");
        }
    }

    private async Task QueueEventAsync(
        FileSystemEventType eventType,
        string fullPath,
        string? oldFullPath,
        CancellationToken cancellationToken)
    {
        var fileEvent = new FileSystemEvent(eventType, fullPath, oldFullPath, _clock.UtcNow.UtcDateTime);

        try
        {
            await _eventChannel.Writer.WriteAsync(fileEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("File system monitoring operation canceled while writing to channel.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while queuing file system monitoring event.");
        }
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await _maintenanceCoordinator.WaitForResumeAsync(cancellationToken).ConfigureAwait(false);
                await _pauseCoordinator.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

                // èteme bez tokenu; pøi shutdownu se kanál zavøe a skonèíme pomocí ChannelClosedException
                var initialEvent = await _eventChannel.Reader.ReadAsync().ConfigureAwait(false);

                var buffer = new List<FileSystemEvent> { initialEvent };
                var debounceUntil = _clock.UtcNow + _debounceWindow;

                while (true)
                {
                    var remaining = debounceUntil - _clock.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    var waitTask = _eventChannel.Reader.WaitToReadAsync().AsTask();
                    var completed = await Task.WhenAny(Task.Delay(remaining, cancellationToken), waitTask)
                        .ConfigureAwait(false);

                    if (completed != waitTask || !await waitTask.ConfigureAwait(false))
                    {
                        break;
                    }

                    while (_eventChannel.Reader.TryRead(out var nextEvent))
                    {
                        buffer.Add(nextEvent);
                    }
                }

                await ProcessBufferAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("File system monitoring cancelled.");
                break;
            }
            catch (ChannelClosedException)
            {
                _logger.LogDebug("File system monitoring channel closed.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file system events.");
            }
        }
    }

    private async Task ProcessBufferAsync(List<FileSystemEvent> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Count == 0)
        {
            return;
        }

        var coalesced = new Dictionary<string, FileSystemEvent>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileEvent in buffer.OrderBy(e => e.OccurredUtc))
        {
            if (coalesced.TryGetValue(fileEvent.FullPath, out var existing))
            {
                coalesced[fileEvent.FullPath] = MergeEvents(existing, fileEvent);
            }
            else
            {
                coalesced[fileEvent.FullPath] = fileEvent;
            }
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<DomainClock>();
        var hashCalculator = scope.ServiceProvider.GetRequiredService<IFileHashCalculator>();
        var syncActions = new List<FileSystemSyncAction>();

        foreach (var fileEvent in coalesced.Values.OrderBy(e => e.OccurredUtc))
        {
            switch (fileEvent.EventType)
            {
                case FileSystemEventType.Created:
                    await HandleCreatedAsync(
                            fileEvent.FullPath,
                            dbContext,
                            clock,
                            hashCalculator,
                            syncActions,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case FileSystemEventType.Deleted:
                    await HandleDeletedAsync(
                            fileEvent.FullPath,
                            dbContext,
                            clock,
                            syncActions,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case FileSystemEventType.Renamed:
                    await HandleRenamedAsync(
                            fileEvent.OldFullPath,
                            fileEvent.FullPath,
                            dbContext,
                            clock,
                            syncActions,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case FileSystemEventType.Changed:
                    await HandleChangedAsync(
                            fileEvent.FullPath,
                            dbContext,
                            clock,
                            hashCalculator,
                            syncActions,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await DispatchSyncActionsAsync(syncActions, cancellationToken).ConfigureAwait(false);
    }

    private static FileSystemEvent MergeEvents(FileSystemEvent existing, FileSystemEvent incoming)
    {
        if (incoming.EventType is FileSystemEventType.Deleted or FileSystemEventType.Renamed)
        {
            return incoming;
        }

        if (existing.EventType == FileSystemEventType.Renamed)
        {
            return new FileSystemEvent(existing.EventType, existing.FullPath, existing.OldFullPath, incoming.OccurredUtc);
        }

        return incoming;
    }

    // HandleCreatedAsync, HandleDeletedAsync, HandleRenamedAsync, HandleChangedAsync, TryGetRelativePath
    // a DispatchSyncActionsAsync nechávám beze zmìny – už správnì propagují cancellationToken.

    private async Task HandleCreatedAsync(
        string fullPath,
        AppDbContext dbContext,
        DomainClock clock,
        IFileHashCalculator hashCalculator,
        List<FileSystemSyncAction> syncActions,
        CancellationToken cancellationToken)
    {
        if (!TryGetRelativePath(fullPath, out var relativePath))
        {
            return;
        }

        var relativeFilePath = RelativeFilePath.From(relativePath);

        var entity = await dbContext.FileSystems
            .SingleOrDefaultAsync(f => f.RelativePath == relativeFilePath, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            _logger.LogInformation(
                "Detected creation for {RelativePath} but no matching file system entity exists (future import may handle).",
                relativePath);
            return;
        }

        var wasMissing = entity.IsMissing;

        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            _logger.LogDebug("Detected creation for {RelativePath} but file is not present on disk.", relativePath);
            return;
        }

        var size = ByteSize.From(info.Length);
        var createdUtc = UtcTimestamp.From(info.CreationTimeUtc);
        var lastWriteUtc = UtcTimestamp.From(info.LastWriteTimeUtc);
        var lastAccessUtc = UtcTimestamp.From(info.LastAccessTimeUtc);
        var observedUtc = UtcTimestamp.From(clock.UtcNow);

        FileHash computedHash = default;
        var hashComputed = false;

        try
        {
            computedHash = await hashCalculator.ComputeSha256Async(fullPath, cancellationToken).ConfigureAwait(false);
            hashComputed = true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to compute hash for created path {RelativePath}; existing hash will be preserved.", relativePath);
        }

        entity.MarkHealthy();
        entity.UpdatePath(fullPath);
        if (hashComputed)
        {
            entity.ReplaceContent(entity.RelativePath, computedHash, size, entity.Mime, entity.IsEncrypted, observedUtc);
        }
        else if (entity.Size != size)
        {
            entity.MarkContentChanged();
            syncActions.Add(new FileSystemSyncAction(FileSystemSyncEvent.ContentChanged, entity.Id));
        }

        entity.UpdateTimestamps(createdUtc, lastWriteUtc, lastAccessUtc, observedUtc);

        if (wasMissing)
        {
            syncActions.Add(new FileSystemSyncAction(FileSystemSyncEvent.Rehydrated, entity.Id));
        }
    }

    private async Task HandleDeletedAsync(
        string fullPath,
        AppDbContext dbContext,
        DomainClock clock,
        List<FileSystemSyncAction> syncActions,
        CancellationToken cancellationToken)
    {
        if (!TryGetRelativePath(fullPath, out var relativePath))
        {
            return;
        }

        var relativeFilePath = RelativeFilePath.From(relativePath);

        var entity = await dbContext.FileSystems
            .SingleOrDefaultAsync(f => f.RelativePath == relativeFilePath, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            _logger.LogDebug("No tracked file system entity found for deleted path {RelativePath}.", relativePath);
            return;
        }

        entity.MarkMissing(clock);
        syncActions.Add(new FileSystemSyncAction(FileSystemSyncEvent.Missing, entity.Id));
    }

    private async Task HandleRenamedAsync(
        string? oldFullPath,
        string newFullPath,
        AppDbContext dbContext,
        DomainClock clock,
        List<FileSystemSyncAction> syncActions,
        CancellationToken cancellationToken)
    {
        if (!TryGetRelativePath(newFullPath, out var newRelativePath))
        {
            return;
        }

        string? oldRelativePath = null;
        if (!string.IsNullOrWhiteSpace(oldFullPath) && TryGetRelativePath(oldFullPath, out var resolvedOld))
        {
            oldRelativePath = resolvedOld;
        }

        var oldRelativeFilePath = oldRelativePath is null ? null : RelativeFilePath.From(oldRelativePath);

        FileSystemEntity? entity = null;

        if (oldRelativeFilePath is not null)
        {
            entity = await dbContext.FileSystems
                .SingleOrDefaultAsync(f => f.RelativePath == oldRelativeFilePath, cancellationToken)
                .ConfigureAwait(false);
        }

        if (entity is null && !string.IsNullOrWhiteSpace(oldFullPath))
        {
            entity = await dbContext.FileSystems
                .Where(f => f.CurrentFilePath == oldFullPath)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (entity is null)
        {
            _logger.LogDebug(
                "No tracked file system entity found for rename from {OldRelativePath} to {NewRelativePath}.",
                oldRelativePath ?? oldFullPath,
                newRelativePath);
            return;
        }

        var whenUtc = UtcTimestamp.From(clock.UtcNow);
        var newRelativeFilePath = RelativeFilePath.From(newRelativePath);
        entity.MoveTo(newRelativeFilePath, whenUtc);
        entity.MarkMovedOrRenamed(newFullPath);

        var info = new FileInfo(newFullPath);
        if (info.Exists)
        {
            entity.UpdateTimestamps(
                UtcTimestamp.From(info.CreationTimeUtc),
                UtcTimestamp.From(info.LastWriteTimeUtc),
                UtcTimestamp.From(info.LastAccessTimeUtc),
                whenUtc);
        }

        syncActions.Add(new FileSystemSyncAction(FileSystemSyncEvent.Moved, entity.Id));
    }

    private async Task HandleChangedAsync(
        string fullPath,
        AppDbContext dbContext,
        DomainClock clock,
        IFileHashCalculator hashCalculator,
        List<FileSystemSyncAction> syncActions,
        CancellationToken cancellationToken)
    {
        if (!TryGetRelativePath(fullPath, out var relativePath))
        {
            return;
        }

        var relativeFilePath = RelativeFilePath.From(relativePath);

        var entity = await dbContext.FileSystems
            .SingleOrDefaultAsync(f => f.RelativePath == relativeFilePath, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            _logger.LogDebug("Change notification ignored; no file system entity found for {RelativePath}.", relativePath);
            return;
        }

        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            _logger.LogDebug("Change notification treated as deletion for {RelativePath} because file is missing.", relativePath);
            await HandleDeletedAsync(fullPath, dbContext, clock, syncActions, cancellationToken).ConfigureAwait(false);
            return;
        }

        var whenUtc = UtcTimestamp.From(clock.UtcNow);
        var size = ByteSize.From(info.Length);
        var createdUtc = UtcTimestamp.From(info.CreationTimeUtc);
        var lastWriteUtc = UtcTimestamp.From(info.LastWriteTimeUtc);
        var lastAccessUtc = UtcTimestamp.From(info.LastAccessTimeUtc);

        var sizeChanged = entity.Size != size;
        var timestampsChanged = entity.CreatedUtc != createdUtc ||
                                entity.LastWriteUtc != lastWriteUtc ||
                                entity.LastAccessUtc != lastAccessUtc;

        FileHash computedHash = default;
        var hashComputed = false;
        var contentChanged = false;

        if (sizeChanged)
        {
            try
            {
                computedHash = await hashCalculator.ComputeSha256Async(fullPath, cancellationToken)
                    .ConfigureAwait(false);
                hashComputed = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to recompute hash for {RelativePath}; marking content changed based on size.", relativePath);
            }
        }

        if (hashComputed)
        {
            entity.ReplaceContent(entity.RelativePath, computedHash, size, entity.Mime, entity.IsEncrypted, whenUtc);
            contentChanged = true;
        }
        else if (sizeChanged)
        {
            entity.MarkContentChanged();
            contentChanged = true;
        }

        entity.UpdatePath(fullPath);
        entity.UpdateTimestamps(createdUtc, lastWriteUtc, lastAccessUtc, whenUtc);

        if (!sizeChanged && timestampsChanged)
        {
            entity.MarkHealthy();
        }

        if (contentChanged)
        {
            syncActions.Add(new FileSystemSyncAction(FileSystemSyncEvent.ContentChanged, entity.Id));
        }
    }

    private bool TryGetRelativePath(string fullPath, out string? relativePath)
    {
        relativePath = null;

        try
        {
            relativePath = _pathResolver.GetRelativePath(fullPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to resolve relative path for {FullPath}; ignoring watcher event.", fullPath);
            return false;
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
                        await _syncService.HandleFileMissingAsync(action.FileSystemId, cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case FileSystemSyncEvent.Rehydrated:
                        await _syncService.HandleFileRehydratedAsync(action.FileSystemId, cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case FileSystemSyncEvent.Moved:
                        await _syncService.HandleFileMovedAsync(action.FileSystemId, cancellationToken)
                            .ConfigureAwait(false);
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
                    "Unhandled exception while coordinating logical file updates for file system id {FileSystemId}.",
                    action.FileSystemId);
            }
        }
    }
}

internal sealed record FileSystemSyncAction(FileSystemSyncEvent EventType, Guid FileSystemId);

internal enum FileSystemSyncEvent
{
    Missing,
    Rehydrated,
    Moved,
    ContentChanged,
}

internal enum FileSystemEventType
{
    Created,
    Deleted,
    Renamed,
    Changed,
}

internal sealed class FileSystemEvent
{
    public FileSystemEvent(FileSystemEventType eventType, string fullPath, string? oldFullPath, DateTime occurredUtc)
    {
        EventType = eventType;
        FullPath = fullPath ?? throw new ArgumentNullException(nameof(fullPath));
        OldFullPath = oldFullPath;
        OccurredUtc = occurredUtc;
    }

    public FileSystemEventType EventType { get; }

    public string FullPath { get; }

    public string? OldFullPath { get; }

    public DateTime OccurredUtc { get; }
}
