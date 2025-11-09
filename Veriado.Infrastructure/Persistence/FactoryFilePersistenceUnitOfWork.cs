using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides a factory-backed unit of work that owns its DbContext instance.
/// </summary>
internal sealed class FactoryFilePersistenceUnitOfWork : IFactoryFilePersistenceUnitOfWork
{
    private readonly EfFilePersistenceUnitOfWork _inner;

    public FactoryFilePersistenceUnitOfWork(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var context = dbContextFactory.CreateDbContext();
        var logger = loggerFactory.CreateLogger<EfFilePersistenceUnitOfWork>();
        _inner = new EfFilePersistenceUnitOfWork(context, logger, ownsContext: true);
    }

    public Task<bool> HasTrackedChangesAsync(CancellationToken cancellationToken)
        => _inner.HasTrackedChangesAsync(cancellationToken);

    public Task<IFilePersistenceTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        => _inner.BeginTransactionAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
        => _inner.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
