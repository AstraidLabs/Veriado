using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides pooled access to SQLite connections used by infrastructure services.
/// </summary>
internal interface ISqliteConnectionFactory
{
    /// <summary>
    /// Rents a SQLite connection from the shared pool.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for opening the connection.</param>
    /// <returns>A lease that returns the connection to the pool when disposed.</returns>
    ValueTask<SqliteConnectionLease> CreateConnectionAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Represents a pooled SQLite connection lease.
/// </summary>
internal sealed class SqliteConnectionLease : IAsyncDisposable
{
    private readonly PooledSqliteConnectionFactory _factory;
    private SqliteConnection? _connection;

    internal SqliteConnectionLease(PooledSqliteConnectionFactory factory, SqliteConnection connection)
    {
        _factory = factory;
        _connection = connection;
    }

    /// <summary>
    /// Gets the leased SQLite connection instance.
    /// </summary>
    public SqliteConnection Connection
        => _connection ?? throw new ObjectDisposedException(nameof(SqliteConnectionLease));

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is null)
        {
            return ValueTask.CompletedTask;
        }

        return _factory.ReturnAsync(connection);
    }
}

/// <summary>
/// Provides a pooled implementation of <see cref="ISqliteConnectionFactory"/>.
/// </summary>
internal sealed class PooledSqliteConnectionFactory : ISqliteConnectionFactory, IAsyncDisposable
{
    private const int DefaultMaxPoolSize = 64;

    private readonly InfrastructureOptions _options;
    private readonly ConcurrentBag<SqliteConnection> _pool = new();
    private readonly int _maxPoolSize;

    private int _poolCount;
    private bool _disposed;

    public PooledSqliteConnectionFactory(InfrastructureOptions options)
        : this(options, DefaultMaxPoolSize)
    {
    }

    public PooledSqliteConnectionFactory(InfrastructureOptions options, int maxPoolSize)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
        }

        if (maxPoolSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPoolSize));
        }

        _maxPoolSize = maxPoolSize;
    }

    /// <inheritdoc />
    public ValueTask<SqliteConnectionLease> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PooledSqliteConnectionFactory));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var connection = RentConnection();
        return new ValueTask<SqliteConnectionLease>(new SqliteConnectionLease(this, connection));
    }

    private SqliteConnection RentConnection()
    {
        if (_pool.TryTake(out var connection))
        {
            Interlocked.Decrement(ref _poolCount);
            return connection;
        }

        return new SqliteConnection(_options.ConnectionString);
    }

    internal async ValueTask ReturnAsync(SqliteConnection connection)
    {
        if (connection is null)
        {
            return;
        }

        if (_disposed)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (connection.State != System.Data.ConnectionState.Closed)
        {
            connection.Close();
        }

        if (Interlocked.Increment(ref _poolCount) <= _maxPoolSize)
        {
            _pool.Add(connection);
            return;
        }

        Interlocked.Decrement(ref _poolCount);
        await connection.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        while (_pool.TryTake(out var connection))
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
