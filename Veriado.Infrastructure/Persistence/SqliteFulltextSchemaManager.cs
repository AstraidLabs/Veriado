using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Persistence.Schema;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides helpers to enforce and maintain the unified contentless FTS5 schema.
/// </summary>
internal static class SqliteFulltextSchemaManager
{
    public static async Task<bool> EnsureUnifiedSchemaAsync(
        SqliteConnection connection,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var inspection = await SqliteFulltextSchemaInspector
            .InspectAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        SqliteFulltextSupport.UpdateSchemaSnapshot(inspection.Snapshot);

        var requiresReset = !inspection.Snapshot.IsContentless
            || inspection.MissingFtsColumns.Count > 0
            || inspection.MissingDocumentColumns.Count > 0
            || inspection.MissingTriggers.Count > 0;

        if (!requiresReset)
        {
            logger?.LogDebug("search_document_fts schema already matches the unified contentless configuration.");
            return false;
        }

        var reason = inspection.FailureReason ?? "Detected legacy content-linked FTS schema.";
        logger?.LogWarning(
            "Resetting search_document_fts to unified contentless schema. Reason: {Reason}",
            reason);

        await ExecuteStatementsAsync(connection, SqliteFulltextSchemaSql.ResetStatements, logger, cancellationToken)
            .ConfigureAwait(false);
        await ExecuteStatementsAsync(connection, SqliteFulltextSchemaSql.CreateStatements, logger, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSearchDocumentColumnsAsync(connection, logger, cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, SqliteFulltextSchemaSql.PopulateStatement, logger, cancellationToken)
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, SqliteFulltextSchemaSql.OptimizeStatement, logger, cancellationToken)
            .ConfigureAwait(false);

        var updated = await SqliteFulltextSchemaInspector
            .InspectAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        SqliteFulltextSupport.UpdateSchemaSnapshot(updated.Snapshot);

        if (!updated.IsValid)
        {
            var updatedReason = updated.FailureReason ?? "Unknown mismatch after rebuild.";
            logger?.LogError(
                "Unified FTS schema enforcement failed: {Reason}",
                updatedReason);
            throw new InvalidOperationException($"Failed to enforce unified FTS schema: {updatedReason}");
        }

        logger?.LogInformation("Unified contentless FTS schema applied successfully.");
        return true;
    }

    public static async Task<int> ReindexAsync(SqliteConnection connection, ILogger? logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var totalChanges = 0;
        foreach (var statement in SqliteFulltextSchemaSql.ReindexStatements)
        {
            totalChanges += await ExecuteNonQueryAsync(connection, statement, logger, cancellationToken)
                .ConfigureAwait(false);
        }

        return totalChanges;
    }

    public static Task ApplyFullResetAsync(SqliteConnection connection, ILogger? logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return ExecuteStatementsAsync(connection, SqliteFulltextSchemaSql.FullResetStatements, logger, cancellationToken);
    }

    private static async Task ExecuteStatementsAsync(
        SqliteConnection connection,
        IReadOnlyList<string> statements,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (statements.Count == 0)
        {
            return;
        }

        for (var i = 0; i < statements.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sql = statements[i];
            if (string.IsNullOrWhiteSpace(sql))
            {
                continue;
            }

            logger?.LogDebug(
                "Executing FTS schema statement {Index}/{Total}: {Sql}",
                i + 1,
                statements.Count,
                sql);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return 0;
        }

        logger?.LogDebug("Executing FTS schema command: {Sql}", sql);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSearchDocumentColumnsAsync(
        SqliteConnection connection,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var existingColumns = await GetSearchDocumentColumnsAsync(connection, cancellationToken).ConfigureAwait(false);

        foreach (var (columnName, columnDefinition) in SqliteFulltextSchemaSql.SearchDocumentColumnDefinitions)
        {
            if (existingColumns.Contains(columnName))
            {
                continue;
            }

            logger?.LogDebug("Adding missing search_document column: {Column}", columnName);

            var statement = $"ALTER TABLE search_document ADD COLUMN {columnDefinition};";
            await ExecuteNonQueryAsync(connection, statement, logger, cancellationToken).ConfigureAwait(false);
            existingColumns.Add(columnName);
        }
    }

    private static async Task<HashSet<string>> GetSearchDocumentColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(search_document);";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(1))
            {
                columns.Add(reader.GetString(1));
            }
        }

        return columns;
    }
}
