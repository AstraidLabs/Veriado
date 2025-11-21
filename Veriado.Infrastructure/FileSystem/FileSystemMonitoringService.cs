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
using Veriado.Appl.FileSystem;
using Veriado.Domain.FileSystem;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence;
using ApplicationClock = Veriado.Appl.Abstractions.IClock;
using DomainClock = Veriado.Domain.Primitives.IClock;

namespace Veriado.Infrastructure.FileSystem;

internal sealed class FileSystemMonitoringService : IFileSystemMonitoringService
{
    private readonly IFilePathResolver _pathResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOperationalPauseCoordinator _pauseCoordinator;
    private readonly IFileSystemSyncService _syncService;
    private readonly ApplicationClock _clock;
    private readonly ILogger<FileSystemMonitoringService> _logger;
    private readonly Channel<FileSystemEvent> _eventChannel;
    private readonly TimeSpan _debounceWindow = TimeSpan.FromMilliseconds(250);
    private Task? _processingTask;
    private FileSystemWatcher? _watcher;
    private CancellationToken _cancellationToken;

    public FileSystemMonitoringService(
        IFilePathResolver pathResolver,
        IServiceScopeFactory scopeFactory,
        IOperationalPauseCoordinator pauseCoordinator,
        IFileSystemSyncService syncService,
        ApplicationClock clock,
        ILogger<FileSystemMonitoringService> logger)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _pauseCoordinator = pauseCoordinator ?? throw new ArgumentNullException(nameof(pauseCoordinator));
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

    public void Start(CancellationToken cancellationToken)
    {
        if (_watcher is not null)
        {
            _logger.LogDebug("File system monitoring already started.");
            return;
        }

        _cancellationToken = cancellationToken;

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

        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
        };

        _watcher.Created += OnCreated;
        _watcher.Deleted += OnDeleted;
        _watcher.Renamed += OnRenamed;
        _watcher.Changed += OnChanged;
        _watcher.EnableRaisingEvents = true;

        _processingTask ??= Task.Run(() => ProcessEventsAsync(_cancellationToken), _cancellationToken);

        _logger.LogInformation("File system monitoring started for root {Root}.", root);
    }

    public void Dispose()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnCreated;
        _watcher.Deleted -= OnDeleted;
        _watcher.Renamed -= OnRenamed;
        _watcher.Changed -= OnChanged;
        _watcher.Dispose();
        _watcher = null;

        _eventChannel.Writer.TryComplete();

        try
        {
            _processingTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
        }
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        QueueEvent(FileSystemEventType.Created, e.FullPath, null);
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        QueueEvent(FileSystemEventType.Deleted, e.FullPath, null);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        QueueEvent(FileSystemEventType.Renamed, e.FullPath, e.OldFullPath);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        QueueEvent(FileSystemEventType.Changed, e.FullPath, null);
    }

    private void QueueEvent(FileSystemEventType eventType, string fullPath, string? oldFullPath)
    {
        var fileEvent = new FileSystemEvent(eventType, fullPath, oldFullPath, _clock.UtcNow.UtcDateTime);

        _ = Task.Run(async () =>
        {
            try
            {
                await _eventChannel.Writer.WriteAsync(fileEvent, _cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("File system monitoring operation canceled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception while queuing file system monitoring event.");
            }
        }, _cancellationToken);
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _pauseCoordinator.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

                var initialEvent = await _eventChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new List<FileSystemEvent> { initialEvent };
                var debounceUntil = _clock.UtcNow + _debounceWindow;

                while (true)
                {
                    var remaining = debounceUntil - _clock.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    var waitTask = _eventChannel.Reader.WaitToReadAsync(cancellationToken).AsTask();
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
                break;
            }
            catch (ChannelClosedException)
            {
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

        FileHash? hash = null;

        try
        {
            hash = await hashCalculator.ComputeSha256Async(fullPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to compute hash for created path {RelativePath}; existing hash will be preserved.", relativePath);
        }

        entity.MarkHealthy();
        entity.UpdatePath(fullPath);
        if (hash is not null)
        {
            entity.ReplaceContent(entity.RelativePath, hash, size, entity.Mime, entity.IsEncrypted, observedUtc);
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
        var timestampsChanged = entity.CreatedUtc != createdUtc || entity.LastWriteUtc != lastWriteUtc || entity.LastAccessUtc != lastAccessUtc;

        FileHash? hash = null;
        var contentChanged = false;

        if (sizeChanged)
        {
            try
            {
                hash = await hashCalculator.ComputeSha256Async(fullPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to recompute hash for {RelativePath}; marking content changed based on size.", relativePath);
            }
        }

        if (hash is not null)
        {
            entity.ReplaceContent(entity.RelativePath, hash, size, entity.Mime, entity.IsEncrypted, whenUtc);
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
                        await _syncService.HandleFileMissingAsync(action.FileSystemId, cancellationToken).ConfigureAwait(false);
                        break;
                    case FileSystemSyncEvent.Rehydrated:
                        await _syncService.HandleFileRehydratedAsync(action.FileSystemId, cancellationToken).ConfigureAwait(false);
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
