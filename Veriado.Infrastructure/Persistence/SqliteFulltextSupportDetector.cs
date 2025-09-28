namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides helper methods to detect SQLite full-text search capabilities.
/// </summary>
internal static class SqliteFulltextSupportDetector
{
    public static void Detect(InfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            options.IsFulltextAvailable = false;
            options.FulltextAvailabilityError = "Infrastructure connection string has not been initialised.";
            SqliteFulltextSupport.Update(false, options.FulltextAvailabilityError);
            return;
        }

        var (available, reason) = Probe(options.ConnectionString);
        options.IsFulltextAvailable = available;
        options.FulltextAvailabilityError = reason;
        SqliteFulltextSupport.Update(available, reason);
    }

    private static (bool Available, string? Reason) Probe(string connectionString)
    {
        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using (var moduleCheck = connection.CreateCommand())
            {
                moduleCheck.CommandText = "SELECT 1 FROM pragma_module_list WHERE name = 'fts5' LIMIT 1;";
                var result = moduleCheck.ExecuteScalar();
                if (result is null)
                {
                    return (false, "SQLite build does not include the FTS5 module.");
                }
            }

            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE VIRTUAL TABLE temp.__fts5_probe USING fts5(x, tokenize='unicode61 remove_diacritics 2');";
            try
            {
                command.ExecuteNonQuery();
                command.CommandText = "DROP TABLE temp.__fts5_probe;";
                command.ExecuteNonQuery();
                return (true, null);
            }
            catch (SqliteException ex)
            {
                try
                {
                    command.CommandText = "DROP TABLE IF EXISTS temp.__fts5_probe;";
                    command.ExecuteNonQuery();
                }
                catch
                {
                    // Ignore cleanup failures.
                }

                var reason = ex.Message.Contains("remove_diacritics", StringComparison.OrdinalIgnoreCase)
                    ? "SQLite FTS5 unicode61 tokenizer does not support remove_diacritics=2."
                    : $"SQLite FTS5 tokenizer configuration is not supported: {ex.Message}";
                return (false, reason);
            }
        }
        catch (SqliteException ex)
        {
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
