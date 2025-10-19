using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Veriado.Appl.Abstractions;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Search;

internal sealed class SearchProjectionScopeEf : ISearchProjectionScope
{
    private readonly AppDbContext _dbContext;

    public SearchProjectionScopeEf(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public void EnsureActive()
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Search projection operations require an active EF Core transaction.");
        }
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action(cancellationToken);
    }
}
