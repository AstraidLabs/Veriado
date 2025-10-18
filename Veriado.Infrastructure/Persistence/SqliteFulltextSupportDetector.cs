using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Persistence.Connections;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides helper methods to detect SQLite full-text search capabilities.
/// </summary>
internal static class SqliteFulltextSupportDetector
{
    public static void Detect(
        InfrastructureOptions options,
        IConnectionStringProvider connectionStringProvider,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionStringProvider);

        var (available, reason) = Probe(connectionStringProvider, logger);
        options.IsFulltextAvailable = available;
        options.FulltextAvailabilityError = reason;
        SqliteFulltextSupport.Update(available, reason);
    }

    private static (bool Available, string? Reason) Probe(IConnectionStringProvider provider, ILogger? logger)
    {
        try
        {
            using var connection = provider.CreateConnection();
            connection.Open();
            SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

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

                var inspection = SqliteFulltextSchemaInspector
                    .InspectAsync(connection, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                SqliteFulltextSupport.UpdateSchemaSnapshot(inspection.Snapshot);

                if (!inspection.IsValid)
                {
                    logger?.LogWarning(
                        "FTS schema inspection failed during bootstrap: {Reason}. Attempting to enforce unified schema.",
                        inspection.FailureReason);

                    try
                    {
                        SqliteFulltextSchemaManager
                            .EnsureUnifiedSchemaAsync(connection, logger, CancellationToken.None)
                            .GetAwaiter()
                            .GetResult();
                    }
                    catch (Exception rebuildEx)
                    {
                        logger?.LogError(rebuildEx, "Failed to enforce unified FTS schema during detection.");
                        return (false, rebuildEx.Message);
                    }

                    inspection = SqliteFulltextSchemaInspector
                        .InspectAsync(connection, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    SqliteFulltextSupport.UpdateSchemaSnapshot(inspection.Snapshot);

                    if (!inspection.IsValid)
                    {
                        return (false, inspection.FailureReason);
                    }
                }

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
