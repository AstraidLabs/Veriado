using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Veriado.Infrastructure.Repositories;

/// <summary>
/// Provides persistence operations for file aggregates using direct EF Core transactions.
/// </summary>
internal sealed class FileRepository : IFileRepository
{
    private readonly AppDbContext _db;
    private readonly IDbContextFactory<ReadOnlyDbContext> _readFactory;

    public FileRepository(
        AppDbContext db,
        IDbContextFactory<ReadOnlyDbContext> readFactory)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _readFactory = readFactory ?? throw new ArgumentNullException(nameof(readFactory));
    }

    public async Task<FileEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Files
            .Include(f => f.Validity)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity;
    }

    public async Task<FileSystemEntity?> GetFileSystemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.FileSystems
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity;
    }

    public async Task<IReadOnlyList<FileEntity>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var idList = ids.Distinct().ToArray();
        if (idList.Length == 0)
        {
            return Array.Empty<FileEntity>();
        }

        var files = await _db.Files
            .Include(f => f.Validity)
            .Where(f => idList.Contains(f.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return files;
    }

    public async IAsyncEnumerable<FileEntity> StreamAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var context = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = context.Files
            .Include(f => f.Validity)
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken);

        await foreach (var file in query.ConfigureAwait(false))
        {
            yield return file;
        }
    }

    public async Task<bool> ExistsByHashAsync(FileHash hash, CancellationToken cancellationToken)
    {
        await using var context = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Files.AnyAsync(f => f.ContentHash == hash, cancellationToken).ConfigureAwait(false);
    }

    public Task AddAsync(FileEntity entity, FilePersistenceOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _ = options;
        _db.Files.Add(entity);
        return Task.CompletedTask;
    }

    public Task AddAsync(
        FileEntity file,
        FileSystemEntity fileSystem,
        FilePersistenceOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _ = options;

        _db.FileSystems.Add(fileSystem);
        _db.Files.Add(file);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(FileEntity entity, FilePersistenceOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _ = options;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(
        FileEntity file,
        FileSystemEntity fileSystem,
        FilePersistenceOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _ = options;
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Files.FirstOrDefaultAsync(f => f.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        _db.Files.Remove(entity);
    }

    // TODO(FTS Sync Phase 2): write use-case checklist for synchronous projection integration.
    // Command → Handler.Handle → Service entry point
    // - CreateFileCommand → CreateFileHandler.Handle → ImportService.ImportFileAsync
    // - CreateFileWithUploadCommand → CreateFileWithUploadHandler.Handle → ImportService.ImportFolderStreamAsync
    // - RenameFileCommand → RenameFileHandler.Handle → FileOperationsService.RenameAsync
    // - UpdateFileMetadataCommand → UpdateFileMetadataHandler.Handle → FileOperationsService.UpdateMetadataAsync (author/mime)
    // - SetFileValidityCommand → SetFileValidityHandler.Handle → FileOperationsService.SetValidityAsync
    // - ClearFileValidityCommand → ClearFileValidityHandler.Handle → FileOperationsService.ClearValidityAsync
    // - ReplaceFileContentCommand → ReplaceFileContentHandler.Handle → FileOperationsService.ReplaceContentAsync
    // - RelinkFileContentCommand → RelinkFileContentHandler.Handle → FileOperationsService.ReplaceContentAsync
    // - ApplySystemMetadataCommand → ApplySystemMetadataHandler.Handle → FileOperationsService.ApplySystemMetadataAsync
    // - Delete: FileRepository.DeleteAsync currently invoked directly (no dedicated handler yet)
    // - Import workflows (ImportService.ImportFileAsync / ImportFolderStreamAsync) orchestrate the create/replace commands above.

}
