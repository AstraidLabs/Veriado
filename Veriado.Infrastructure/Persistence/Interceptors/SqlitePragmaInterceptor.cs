using System;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Veriado.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Applies the required SQLite PRAGMA statements whenever a connection is opened.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private readonly ILogger<SqlitePragmaInterceptor> _logger;

    public SqlitePragmaInterceptor(ILogger<SqlitePragmaInterceptor> logger)
    {
        _logger = logger;
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (connection is not SqliteConnection sqlite)
        {
            throw new InvalidOperationException($"SqlitePragmaInterceptor requires SqliteConnection but received {connection.GetType().FullName}.");
        }

        await SqlitePragmaHelper.ApplyAsync(sqlite, _logger, cancellationToken).ConfigureAwait(false);
    }
}
