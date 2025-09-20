using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Veriado.Domain.Files;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Repositories;

/// <summary>
/// Provides read-only queries using the pooled <see cref="ReadOnlyDbContext"/>.
/// </summary>
internal sealed class FileReadRepository
{
    private readonly IDbContextFactory<ReadOnlyDbContext> _contextFactory;

    public FileReadRepository(IDbContextFactory<ReadOnlyDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<FileEntity>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            count = 20;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Files
            .Include(f => f.Validity)
            .OrderByDescending(f => f.LastModifiedUtc.Value)
            .Take(count)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
