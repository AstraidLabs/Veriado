using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Veriado.Appl.Abstractions;
using Veriado.Domain.Files;

namespace Veriado.Infrastructure.Persistence;

internal sealed class ReadOnlyFileContextAdapter : IReadOnlyFileContext
{
    private readonly ReadOnlyDbContext _context;

    public ReadOnlyFileContextAdapter(ReadOnlyDbContext context)
    {
        _context = context;
    }

    public IQueryable<FileEntity> Files => _context.Files.AsNoTracking();

    public Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        return EntityFrameworkQueryableExtensions.ToListAsync(query, cancellationToken);
    }

    public Task<int> CountAsync(IQueryable<FileEntity> query, CancellationToken cancellationToken)
    {
        return EntityFrameworkQueryableExtensions.CountAsync(query, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _context.DisposeAsync();
    }
}

internal sealed class ReadOnlyFileContextFactory : IReadOnlyFileContextFactory
{
    private readonly IDbContextFactory<ReadOnlyDbContext> _factory;

    public ReadOnlyFileContextFactory(IDbContextFactory<ReadOnlyDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyFileContext> CreateAsync(CancellationToken cancellationToken)
    {
        var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return new ReadOnlyFileContextAdapter(context);
    }
}
