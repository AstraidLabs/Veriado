using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Veriado.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Applies the required SQLite PRAGMA statements whenever a connection is opened.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (connection is not SqliteConnection sqlite)
        {
            return;
        }

        await SqlitePragmaHelper.ApplyAsync(sqlite, cancellationToken).ConfigureAwait(false);
    }
}
