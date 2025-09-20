using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Veriado.Application.Abstractions;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Concurrency;
using Veriado.Infrastructure.Persistence;

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
        return await context.Files
            .Include(f => f.Validity)
            .Include(f => f.Content)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> ExistsByHashAsync(FileHash hash, CancellationToken cancellationToken = default)
    {
        await using var context = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Files.AnyAsync(f => f.Content.Hash == hash, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(FileEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _writeQueue.EnqueueAsync(async (AppDbContext db, CancellationToken ct) =>
        {
            db.Files.Add(entity);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(FileEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _writeQueue.EnqueueAsync(async (AppDbContext db, CancellationToken ct) =>
        {
            db.Files.Update(entity);
            return true;
        }, cancellationToken).ConfigureAwait(false);
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
        }, cancellationToken).ConfigureAwait(false);
    }
}
