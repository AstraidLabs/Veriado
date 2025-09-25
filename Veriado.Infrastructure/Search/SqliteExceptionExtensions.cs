using System;
using Microsoft.Data.Sqlite;

namespace Veriado.Infrastructure.Search;

internal static class SqliteExceptionExtensions
{
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    public static bool IndicatesDatabaseCorruption(this SqliteException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase)
        {
            return true;
        }

        if (exception.SqliteExtendedErrorCode is SqliteCorrupt or SqliteNotADatabase)
        {
            return true;
        }

        return exception.Message.Contains("database disk image is malformed", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("malformed", StringComparison.OrdinalIgnoreCase);
    }
}
