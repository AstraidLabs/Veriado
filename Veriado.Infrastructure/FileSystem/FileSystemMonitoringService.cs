using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.Application.Abstractions;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;
using DomainClock = Veriado.Domain.Primitives.IClock;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.FileSystem;

internal sealed class FileSystemMonitoringService : IFileSystemMonitoringService
{
    private readonly IFilePathResolver _pathResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOperationalPauseCoordinator _pauseCoordinator;
    private readonly ILogger<FileSystemMonitoringService> _logger;
    private FileSystemWatcher? _watcher;
    private CancellationToken _cancellationToken;

    public FileSystemMonitoringService(
        IFilePathResolver pathResolver,
        IServiceScopeFactory scopeFactory,
        IOperationalPauseCoordinator pauseCoordinator,
        ILogger<FileSystemMonitoringService> logger)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _pauseCoordinator = pauseCoordinator ?? throw new ArgumentNullException(nameof(pauseCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        QueueHandling(() => HandleCreatedAsync(e.FullPath, _cancellationToken));
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        QueueHandling(() => HandleDeletedAsync(e.FullPath, _cancellationToken));
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        QueueHandling(() => HandleRenamedAsync(e.OldFullPath, e.FullPath, _cancellationToken));
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        QueueHandling(() => HandleChangedAsync(e.FullPath, _cancellationToken));
    }

    private void QueueHandling(Func<Task> handler)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _pauseCoordinator.WaitIfPausedAsync(_cancellationToken).ConfigureAwait(false);
                await handler().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("File system monitoring operation canceled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while processing file system event.");
            }
        }, _cancellationToken);
    }

    private async Task HandleCreatedAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (!TryGetRelativePath(fullPath, out var relativePath))
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<DomainClock>();

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

        entity.MarkHealthy();
        entity.UpdatePath(fullPath);
        entity.UpdateTimestamps(null, UtcTimestamp.From(clock.UtcNow), null, UtcTimestamp.From(clock.UtcNow));

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleDeletedAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (!TryGetRelativePath(fullPath, out var relativePath))
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<DomainClock>();

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
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleRenamedAsync(string oldFullPath, string newFullPath, CancellationToken cancellationToken)
    {
        if (!TryGetRelativePath(oldFullPath, out var oldRelativePath) ||
            !TryGetRelativePath(newFullPath, out var newRelativePath))
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<DomainClock>();

        var oldRelativeFilePath = RelativeFilePath.From(oldRelativePath);

        var entity = await dbContext.FileSystems
            .SingleOrDefaultAsync(f => f.RelativePath == oldRelativeFilePath, cancellationToken)
            .ConfigureAwait(false)
            ?? await dbContext.FileSystems
                .Where(f => f.CurrentFilePath == oldFullPath)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        if (entity is null)
        {
            _logger.LogDebug(
                "No tracked file system entity found for rename from {OldRelativePath} to {NewRelativePath}.",
                oldRelativePath,
                newRelativePath);
            await HandleDeletedAsync(oldFullPath, cancellationToken).ConfigureAwait(false);
            await HandleCreatedAsync(newFullPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        var whenUtc = UtcTimestamp.From(clock.UtcNow);
        entity.MoveTo(RelativeFilePath.From(newRelativePath), whenUtc);
        entity.MarkMovedOrRenamed(newFullPath);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleChangedAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (!TryGetRelativePath(fullPath, out var relativePath))
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<DomainClock>();

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
        var whenUtc = UtcTimestamp.From(clock.UtcNow);
        var size = ByteSize.From(info.Exists ? info.Length : 0);
        entity.ReplaceContent(entity.RelativePath, entity.Hash, size, entity.Mime, entity.IsEncrypted, whenUtc);
        entity.UpdatePath(fullPath);
        entity.UpdateTimestamps(UtcTimestamp.From(info.CreationTimeUtc), UtcTimestamp.From(info.LastWriteTimeUtc), UtcTimestamp.From(info.LastAccessTimeUtc), whenUtc);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
}
