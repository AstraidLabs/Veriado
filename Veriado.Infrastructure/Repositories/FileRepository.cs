using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Veriado.Appl.Common.Exceptions;
using Veriado.Domain.Files.Events;
using Veriado.Infrastructure.Persistence.Entities;

namespace Veriado.Infrastructure.Repositories;

/// <summary>
/// Provides persistence operations for file aggregates using direct EF Core transactions.
/// </summary>
internal sealed partial class FileRepository : IFileRepository
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
        try
        {
            var entity = await _db.Files
                .Include(f => f.Validity)
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return entity;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw CreateConcurrencyException("retrieving", ex);
        }
    }

    public async Task<FileSystemEntity?> GetFileSystemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _db.FileSystems
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return entity;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw CreateConcurrencyException("retrieving file system metadata for", ex);
        }
    }

    public async Task<IReadOnlyList<FileEntity>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var idList = ids.Distinct().ToArray();
        if (idList.Length == 0)
        {
            return Array.Empty<FileEntity>();
        }

        try
        {
            var files = await _db.Files
                .Include(f => f.Validity)
                .Where(f => idList.Contains(f.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return files;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw CreateConcurrencyException("retrieving", ex);
        }
    }

    public async IAsyncEnumerable<FileEntity> StreamAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var context = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = context.Files
            .Include(f => f.Validity)
            .AsAsyncEnumerable();

        await foreach (var file in StreamAllInternalAsync(query, cancellationToken).ConfigureAwait(false))
        {
            yield return file;
        }

        async IAsyncEnumerable<FileEntity> StreamAllInternalAsync(
            IAsyncEnumerable<FileEntity> source,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var configured = source.WithCancellation(ct).ConfigureAwait(false);
            await using var enumerator = configured.GetAsyncEnumerator();

            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    throw CreateConcurrencyException("streaming", ex);
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }

    public async Task<bool> ExistsByHashAsync(FileHash hash, CancellationToken cancellationToken)
    {
        await using var context = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await context.Files
                .AnyAsync(
                    f => f.Content != null && f.Content.ContentHash == hash,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw CreateConcurrencyException("checking the content hash for", ex);
        }
    }

    public Task AddAsync(FileEntity entity, FilePersistenceOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _ = options;

        try
        {
            _db.Files.Add(entity);
            TrackContentHistory(entity);
            return Task.CompletedTask;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw CreateConcurrencyException("adding", ex);
        }
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

        try
        {
            _db.FileSystems.Add(fileSystem);
            _db.Files.Add(file);
            TrackContentHistory(file);
            return Task.CompletedTask;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw CreateConcurrencyException("adding", ex);
        }
    }

    public Task UpdateAsync(FileEntity entity, FilePersistenceOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _ = options;

        try
        {
            TrackContentHistory(entity);
            return Task.CompletedTask;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw CreateConcurrencyException("updating", ex);
        }
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

        try
        {
            TrackContentHistory(file);
            return Task.CompletedTask;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw CreateConcurrencyException("updating", ex);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _db.Files.FirstOrDefaultAsync(f => f.Id == id, cancellationToken).ConfigureAwait(false);
            if (entity is null)
            {
                return;
            }

            _db.Files.Remove(entity);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw CreateConcurrencyException("deleting", ex);
        }
    }

}

partial class FileRepository
{
    private void TrackContentHistory(FileEntity file)
    {
        if (file.Content is null)
        {
            return;
        }

        var entry = _db.Entry(file);
        var appended = false;

        foreach (var evt in file.DomainEvents.OfType<FileContentLinked>())
        {
            AppendContentLink(evt.FileId, evt.Content);
            appended = true;
        }

        foreach (var evt in file.DomainEvents.OfType<FileContentRelinked>())
        {
            AppendContentLink(evt.FileId, evt.Content);
            appended = true;
        }

        if (!appended && entry.State == EntityState.Added)
        {
            AppendContentLink(file.Id, file.Content!);
        }
    }

    private void AppendContentLink(Guid fileId, FileContentLink link)
    {
        var row = new FileContentLinkRow
        {
            FileId = fileId,
            ContentVersion = link.Version.Value,
            Provider = link.Provider,
            Location = link.Location,
            ContentHash = link.ContentHash.Value,
            SizeBytes = link.Size.Value,
            Mime = link.Mime?.Value,
            CreatedUtc = link.CreatedUtc.Value,
        };

        _db.FileContentLinks.Add(row);
    }

    private static FileConcurrencyException CreateConcurrencyException(string operation, Exception innerException)
        => new($"A concurrency conflict occurred while {operation} the file.", innerException);
}
