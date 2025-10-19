using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Persistence.Connections;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides helper methods to detect SQLite full-text search capabilities.
/// </summary>
internal static class SqliteFulltextSupportDetector
{
    public static async Task DetectAsync(
        InfrastructureOptions options,
        IConnectionStringProvider connectionStringProvider,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionStringProvider);

        var (available, reason) = await ProbeAsync(connectionStringProvider, logger, cancellationToken)
            .ConfigureAwait(false);
        options.IsFulltextAvailable = available;
        options.FulltextAvailabilityError = reason;
        SqliteFulltextSupport.Update(available, reason);
    }

    private static async Task<(bool Available, string? Reason)> ProbeAsync(
        IConnectionStringProvider provider,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = provider.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await SqlitePragmaHelper.ApplyAsync(connection, logger, cancellationToken).ConfigureAwait(false);

            using (var moduleCheck = connection.CreateCommand())
            {
                moduleCheck.CommandText = "SELECT 1 FROM pragma_module_list WHERE name = 'fts5' LIMIT 1;";
                var result = await moduleCheck.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
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
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                command.CommandText = "DROP TABLE temp.__fts5_probe;";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                var inspection = await SqliteFulltextSchemaInspector
                    .InspectAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
                SqliteFulltextSupport.UpdateSchemaSnapshot(inspection.Snapshot);

                if (!inspection.IsValid)
                {
                    logger?.LogWarning(
                        "FTS schema inspection failed during bootstrap: {Reason}. Attempting to enforce unified schema.",
                        inspection.FailureReason);

                    try
                    {
                        await SqliteFulltextSchemaManager
                            .EnsureUnifiedSchemaAsync(connection, logger, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception rebuildEx)
                    {
                        logger?.LogError(rebuildEx, "Failed to enforce unified FTS schema during detection.");
                        return (false, rebuildEx.Message);
                    }

                    inspection = await SqliteFulltextSchemaInspector
                        .InspectAsync(connection, cancellationToken)
                        .ConfigureAwait(false);
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
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
