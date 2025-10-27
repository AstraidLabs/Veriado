using System.Data;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Common.Exceptions;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides an EF Core-backed implementation of <see cref="IFilePersistenceUnitOfWork"/>.
/// </summary>
internal sealed class EfFilePersistenceUnitOfWork : IFilePersistenceUnitOfWork
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<EfFilePersistenceUnitOfWork> _logger;
    public EfFilePersistenceUnitOfWork(AppDbContext dbContext, ILogger<EfFilePersistenceUnitOfWork> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool HasTrackedChanges
    {
        get
        {
            var semaphore = _dbContext.SaveChangesSemaphore;
            var lockAcquired = false;

            try
            {
                semaphore.Wait();
                lockAcquired = true;

                return _dbContext.ChangeTracker.HasChanges();
            }
            catch (ObjectDisposedException)
            {
                // EF Core disposes the underlying services when the context is disposed. Some
                // code paths inside ChangeTracker.HasChanges() assume the services are still
                // available and end up throwing when they are not. A disposed context cannot
                // have any tracked changes, so report "no changes" in this situation.
                return false;
            }
            catch (NullReferenceException)
            {
                // EF Core may throw NullReferenceException when ChangeTracker tries to access
                // disposed internal services. Treat this the same as a disposed context.
                return false;
            }
            finally
            {
                if (lockAcquired)
                {
                    semaphore.Release();
                }
            }
        }
    }

    public async Task<IFilePersistenceTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is { } existing)
        {
            return new NestedEfFilePersistenceTransaction(existing, _logger);
        }

        var database = _dbContext.Database;

        try
        {
            var transaction = await database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            return new EfFilePersistenceTransaction(transaction);
        }
        catch (InvalidOperationException ex) when (IsPendingLocalSqliteTransaction(database, ex))
        {
            await ResetSqliteConnectionAsync(database, cancellationToken).ConfigureAwait(false);

            var transaction = await database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            return new EfFilePersistenceTransaction(transaction);
        }

        static bool IsPendingLocalSqliteTransaction(DatabaseFacade database, InvalidOperationException exception)
        {
            if (!database.IsSqlite())
            {
                return false;
            }

            var message = exception.Message;
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            return message.Contains("pending local transaction", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Transaction property of the command has not been initialized", StringComparison.OrdinalIgnoreCase);
        }

        static async Task ResetSqliteConnectionAsync(DatabaseFacade database, CancellationToken cancellationToken)
        {
            if (database.GetDbConnection() is not SqliteConnection sqliteConnection)
            {
                throw new InvalidOperationException("Expected SQLite connection when handling transaction reset.");
            }

            if (sqliteConnection.State != ConnectionState.Closed)
            {
                await sqliteConnection.CloseAsync().ConfigureAwait(false);
            }

            SqliteConnection.ClearPool(sqliteConnection);

            if (sqliteConnection.State != ConnectionState.Closed)
            {
                await sqliteConnection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        var semaphore = _dbContext.SaveChangesSemaphore;
        var lockAcquired = false;

        try
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockAcquired = true;

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
        finally
        {
            if (lockAcquired)
            {
                semaphore.Release();
            }
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
        private readonly ILogger _logger;
        private bool _completed;

        public NestedEfFilePersistenceTransaction(IDbContextTransaction transaction, ILogger logger)
        {
            _ = transaction ?? throw new ArgumentNullException(nameof(transaction));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            // Nested transactions rely on the outer transaction's lifetime; committing here would
            // prematurely complete the underlying transaction.
            _completed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            // The underlying transaction is owned elsewhere. Disposing here would interfere with
            // the outer scope, so the nested wrapper simply no-ops.
            if (!_completed)
            {
                _logger.LogWarning(
                    "NestedEfFilePersistenceTransaction disposed without Commit — relying on outer transaction. This may hide logical errors in inner scopes.");
            }

            return ValueTask.CompletedTask;
        }
    }
}
