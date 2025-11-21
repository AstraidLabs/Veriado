using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Appl.FileSystem;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence;
using ApplicationClock = Veriado.Appl.Abstractions.IClock;

namespace Veriado.Services.FileSystem;

/// <summary>
/// Coordinates logical file state in response to physical file system changes.
/// </summary>
public sealed class FileSystemSyncService : IFileSystemSyncService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ApplicationClock _clock;
    private readonly ILogger<FileSystemSyncService> _logger;

    public FileSystemSyncService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ApplicationClock clock,
        ILogger<FileSystemSyncService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task HandleFileMissingAsync(Guid fileSystemId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var file = await FindFileAsync(dbContext, fileSystemId, cancellationToken).ConfigureAwait(false);

        if (file is null)
        {
            return;
        }

        _logger.LogInformation(
            "Physical file {FileSystemId} is missing; coordination recorded for logical file {FileId}.",
            fileSystemId,
            file.Id);
    }

    public async Task HandleFileRehydratedAsync(Guid fileSystemId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var file = await FindFileAsync(dbContext, fileSystemId, cancellationToken).ConfigureAwait(false);

        if (file is null)
        {
            return;
        }

        _logger.LogInformation(
            "Physical file {FileSystemId} has been restored; logical file {FileId} can be refreshed.",
            fileSystemId,
            file.Id);
    }

    public async Task HandleFileMovedAsync(Guid fileSystemId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var file = await FindFileAsync(dbContext, fileSystemId, cancellationToken).ConfigureAwait(false);

        if (file is null)
        {
            return;
        }

        _logger.LogInformation(
            "Physical file {FileSystemId} moved; logical file {FileId} may need downstream refresh.",
            fileSystemId,
            file.Id);
    }

    public async Task HandleFileContentChangedAsync(Guid fileSystemId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var file = await FindFileAsync(dbContext, fileSystemId, cancellationToken).ConfigureAwait(false);

        if (file is null)
        {
            return;
        }

        file.RequestManualReindex(UtcTimestamp.From(_clock.UtcNow));

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Marked logical file {FileId} for reindex after content change on file system {FileSystemId}.",
            file.Id,
            fileSystemId);
    }

    private async Task<FileEntity?> FindFileAsync(AppDbContext dbContext, Guid fileSystemId, CancellationToken cancellationToken)
    {
        var file = await dbContext.Files
            .SingleOrDefaultAsync(f => f.FileSystemId == fileSystemId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            _logger.LogDebug(
                "No logical file found for file system id {FileSystemId}; coordination step skipped.",
                fileSystemId);
        }

        return file;
    }
}
