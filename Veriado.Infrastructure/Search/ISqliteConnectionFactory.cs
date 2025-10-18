using System;
using System.Collections.Concurrent;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Connections;

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

    /// <summary>
    /// Clears any pooled connections so subsequent rentals create fresh instances.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the reset operation.</param>
    ValueTask ResetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Represents a pooled SQLite connection lease.
/// </summary>
internal sealed class SqliteConnectionLease : IAsyncDisposable
{
    private readonly PooledSqliteConnectionFactory _factory;
    private readonly int _generation;
    private SqliteConnection? _connection;

    internal SqliteConnectionLease(PooledSqliteConnectionFactory factory, SqliteConnection connection, int generation)
    {
        _factory = factory;
        _connection = connection;
        _generation = generation;
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

        return _factory.ReturnAsync(connection, _generation);
    }
}

/// <summary>
/// Provides a pooled implementation of <see cref="ISqliteConnectionFactory"/>.
/// </summary>
internal sealed class PooledSqliteConnectionFactory : ISqliteConnectionFactory, IAsyncDisposable
{
    private sealed record PooledConnection(SqliteConnection Connection, int Generation);

    private const int DefaultMaxPoolSize = 64;

    private readonly InfrastructureOptions _options;
    private readonly IConnectionStringProvider _connectionStringProvider;
    private readonly ConcurrentBag<PooledConnection> _pool = new();
    private readonly int _maxPoolSize;

    private int _poolCount;
    private int _generation;
    private bool _disposed;

    public PooledSqliteConnectionFactory(InfrastructureOptions options, IConnectionStringProvider connectionStringProvider)
        : this(options, connectionStringProvider, DefaultMaxPoolSize)
    {
    }

    public PooledSqliteConnectionFactory(InfrastructureOptions options, IConnectionStringProvider connectionStringProvider, int maxPoolSize)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));

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
        var pooled = RentConnection();
        return new ValueTask<SqliteConnectionLease>(new SqliteConnectionLease(this, pooled.Connection, pooled.Generation));
    }

    private PooledConnection RentConnection()
    {
        if (_pool.TryTake(out var pooled))
        {
            Interlocked.Decrement(ref _poolCount);
            PrepareConnection(pooled.Connection);
            return pooled;
        }

        var generation = Volatile.Read(ref _generation);
        var connection = _connectionStringProvider.CreateConnection();
        PrepareConnection(connection);
        return new PooledConnection(connection, generation);
    }

    private static void PrepareConnection(SqliteConnection connection)
    {
        connection.StateChange -= ApplyPragmasOnOpen;
        connection.StateChange += ApplyPragmasOnOpen;
    }

    private static void ApplyPragmasOnOpen(object? sender, StateChangeEventArgs e)
    {
        if (e.CurrentState != ConnectionState.Open)
        {
            return;
        }

        if (sender is not SqliteConnection connection)
        {
            return;
        }

        SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
    }

    internal async ValueTask ReturnAsync(SqliteConnection connection, int generation)
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

        if (generation != Volatile.Read(ref _generation))
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
            _pool.Add(new PooledConnection(connection, generation));
            return;
        }

        Interlocked.Decrement(ref _poolCount);
        await connection.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ResetAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        Interlocked.Increment(ref _generation);

        while (_pool.TryTake(out var pooled))
        {
            Interlocked.Decrement(ref _poolCount);
            await pooled.Connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        while (_pool.TryTake(out var pooled))
        {
            await pooled.Connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
