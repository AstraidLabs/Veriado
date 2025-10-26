using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Common.Exceptions;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides an EF Core-backed implementation of <see cref="IFilePersistenceUnitOfWork"/>.
/// </summary>
internal sealed class EfFilePersistenceUnitOfWork : IFilePersistenceUnitOfWork
{
    private readonly AppDbContext _dbContext;

    public EfFilePersistenceUnitOfWork(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public bool HasTrackedChanges => _dbContext.ChangeTracker.HasChanges();

    public async Task<IFilePersistenceTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is { } existing)
        {
            return new NestedEfFilePersistenceTransaction(existing);
        }

        var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        return new EfFilePersistenceTransaction(transaction);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new FileConcurrencyException("The file was modified by another operation.", ex);
        }
        catch (DbUpdateException ex) when (IsDuplicateContentHashViolation(ex))
        {
            throw new DuplicateFileContentException("A file with identical content already exists.", ex);
        }
    }

    private static bool IsDuplicateContentHashViolation(DbUpdateException exception)
    {
        if (exception.InnerException is not SqliteException sqlite)
        {
            return false;
        }

        const int SqliteConstraint = 19; // SQLITE_CONSTRAINT
        const int SqliteConstraintUnique = 2067; // SQLITE_CONSTRAINT_UNIQUE

        if (sqlite.SqliteErrorCode != SqliteConstraint)
        {
            return false;
        }

        if (sqlite.SqliteExtendedErrorCode != 0 && sqlite.SqliteExtendedErrorCode != SqliteConstraintUnique)
        {
            return false;
        }

        return sqlite.Message.Contains("files.content_hash", StringComparison.OrdinalIgnoreCase)
            || sqlite.Message.Contains("files_content.hash", StringComparison.OrdinalIgnoreCase)
            || sqlite.Message.Contains("ux_files_content_hash", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class EfFilePersistenceTransaction : IFilePersistenceTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfFilePersistenceTransaction(IDbContextTransaction transaction)
        {
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        }

        public Task CommitAsync(CancellationToken cancellationToken)
            => _transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync()
            => _transaction.DisposeAsync();
    }

    private sealed class NestedEfFilePersistenceTransaction : IFilePersistenceTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public NestedEfFilePersistenceTransaction(IDbContextTransaction transaction)
        {
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            // Nested transactions rely on the outer transaction's lifetime; committing here would
            // prematurely complete the underlying transaction.
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            // The underlying transaction is owned elsewhere. Disposing here would interfere with
            // the outer scope, so the nested wrapper simply no-ops.
            return ValueTask.CompletedTask;
        }
    }
}
