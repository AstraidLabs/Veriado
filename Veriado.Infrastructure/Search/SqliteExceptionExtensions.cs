namespace Veriado.Infrastructure.Search;

internal static class SqliteExceptionExtensions
{
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;
    private const int SqliteCantOpen = 14;
    private const int SqliteSchema = 17;
    private const int PrimaryErrorMask = 0xFF;

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

    public static bool IndicatesFatalFulltextFailure(this SqliteException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.IndicatesDatabaseCorruption() || exception.IndicatesFulltextSchemaMissing())
        {
            return true;
        }

        var primary = exception.GetPrimaryErrorCode();
        return primary is SqliteCantOpen or SqliteSchema;
    }

    public static int GetPrimaryErrorCode(this SqliteException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.SqliteExtendedErrorCode != 0)
        {
            return exception.SqliteExtendedErrorCode & PrimaryErrorMask;
        }

        return exception.SqliteErrorCode;
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
            return ContainsFulltextIdentifier(exception.Message);
        }

        if (exception.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase))
        {
            if (exception.Message.Contains("fts", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("file_search", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ContainsFulltextIdentifier(exception.Message);
        }

        return false;
    }

    private static bool ContainsFulltextIdentifier(string message)
    {
        return message.Contains("file_search", StringComparison.OrdinalIgnoreCase)
            || message.Contains("documentcontent", StringComparison.OrdinalIgnoreCase)
            || message.Contains("document_content", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IndicatesMissingColumn(this SqliteException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.SqliteErrorCode == 1
            && exception.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase);
    }
}
