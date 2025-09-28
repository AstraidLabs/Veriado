using System.Runtime.CompilerServices;
using Veriado.Infrastructure.Concurrency;

namespace Veriado.Infrastructure.Repositories;

/// <summary>
/// Provides persistence operations for file aggregates using the queued write worker.
/// </summary>
internal sealed class FileRepository : IFileRepository
{
    private readonly IWriteQueue _writeQueue;
    private readonly IDbContextFactory<ReadOnlyDbContext> _readFactory;

    public FileRepository(IWriteQueue writeQueue, IDbContextFactory<ReadOnlyDbContext> readFactory)
    {
        _writeQueue = writeQueue;
        _readFactory = readFactory;
    }

    public async Task<FileEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await context.Files
            .Include(f => f.Validity)
            .Include(f => f.Content)
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

        await using var context = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var files = await context.Files
            .Include(f => f.Validity)
            .Include(f => f.Content)
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
            .Include(f => f.Content)
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
        return await context.Files.AnyAsync(f => f.Content.Hash == hash, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(FileEntity entity, FilePersistenceOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var tracked = new[] { new QueuedFileWrite(entity, options) };
        await _writeQueue.EnqueueAsync((AppDbContext db, CancellationToken ct) =>
        {
            db.Files.Add(entity);
            return Task.FromResult(true);
        }, tracked, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(FileEntity entity, FilePersistenceOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var tracked = new[] { new QueuedFileWrite(entity, options) };
        await _writeQueue.EnqueueAsync((AppDbContext db, CancellationToken ct) =>
        {
            db.Files.Update(entity);
            return Task.FromResult(true);
        }, tracked, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _writeQueue.EnqueueAsync(async (AppDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct).ConfigureAwait(false);
            if (entity is null)
            {
                return false;
            }

            db.Files.Remove(entity);
            return true;
        }, null, cancellationToken).ConfigureAwait(false);
    }
}
