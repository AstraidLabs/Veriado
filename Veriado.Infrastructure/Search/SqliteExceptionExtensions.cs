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

    public static bool IndicatesFulltextSchemaMissing(this SqliteException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.SqliteErrorCode != 1 && exception.SqliteErrorCode != 0)
        {
            return false;
        }

        if (exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            return exception.Message.Contains("file_search", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("file_trgm", StringComparison.OrdinalIgnoreCase);
        }

        if (exception.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase))
        {
            return exception.Message.Contains("fts", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("file_search", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("file_trgm", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
