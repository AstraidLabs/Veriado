using System;
using Microsoft.EntityFrameworkCore;
using Veriado.Appl.Abstractions;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides transaction validation for projection operations executed against a specific <see cref="DbContext"/> instance.
/// </summary>
internal sealed class DbContextSearchProjectionGuard : ISearchProjectionTransactionGuard
{
    private readonly DbContext _context;

    public DbContextSearchProjectionGuard(DbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public void EnsureActiveTransaction(DbContext projectionContext)
    {
        ArgumentNullException.ThrowIfNull(projectionContext);

        if (!ReferenceEquals(_context, projectionContext))
        {
            throw new InvalidOperationException("Search projection must use the ambient persistence context transaction.");
        }

        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Search projection operations require an active EF Core transaction.");
        }
    }
}
