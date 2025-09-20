using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Veriado.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Applies the required SQLite PRAGMA statements whenever a connection is opened.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override async ValueTask ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (connection is not SqliteConnection sqlite)
        {
            return;
        }

        await using var command = sqlite.CreateCommand();
        command.CommandText = string.Join(";",
            "PRAGMA journal_mode=WAL",
            "PRAGMA synchronous=NORMAL",
            "PRAGMA foreign_keys=ON",
            "PRAGMA temp_store=MEMORY",
            "PRAGMA mmap_size=134217728",
            "PRAGMA cache_size=-32768",
            "PRAGMA busy_timeout=5000");
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
