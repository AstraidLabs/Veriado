using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides helpers for inspecting the SQLite full-text schema and reporting missing components.
/// </summary>
internal static class SqliteFulltextSchemaInspector
{
    private static readonly string[] ExpectedFtsColumns =
    {
        "title",
        "author",
        "mime",
        "metadata_text",
        "metadata",
    };

    private static readonly string[] ExpectedDocumentColumns =
    {
        "doc_id",
        "file_id",
        "title",
        "author",
        "mime",
        "metadata_text",
        "metadata",
    };

    private static readonly string[] ExpectedTriggers = { "dc_ai", "dc_au", "dc_ad" };

    /// <summary>
    /// Inspects the <c>file_search</c> and <c>DocumentContent</c> schema to detect structural problems.
    /// </summary>
    public static async Task<FulltextSchemaInspectionResult> InspectAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var tableSql = await ReadTableDefinitionAsync(connection, "file_search", cancellationToken).ConfigureAwait(false);
        var ftsColumns = await ReadColumnsAsync(connection, "file_search", cancellationToken).ConfigureAwait(false);
        var documentColumns = await ReadColumnsAsync(connection, "DocumentContent", cancellationToken).ConfigureAwait(false);
        var triggers = await ReadDocumentContentTriggersAsync(connection, cancellationToken).ConfigureAwait(false);

        var isContentless = !string.IsNullOrWhiteSpace(tableSql)
            && !tableSql.Contains("content=", StringComparison.OrdinalIgnoreCase);

        var missingFtsColumns = ExpectedFtsColumns
            .Where(column => !ftsColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var missingDocumentColumns = ExpectedDocumentColumns
            .Where(column => !documentColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var missingTriggers = ExpectedTriggers
            .Where(trigger => !triggers.ContainsKey(trigger))
            .ToArray();

        var reasons = new List<string>();
        if (string.IsNullOrWhiteSpace(tableSql))
        {
            reasons.Add("file_search table definition missing");
        }
        else if (!isContentless)
        {
            reasons.Add("file_search is not contentless FTS5");
        }

        if (missingFtsColumns.Length > 0)
        {
            reasons.Add($"file_search missing columns: {string.Join(", ", missingFtsColumns)}");
        }

        if (missingDocumentColumns.Length > 0)
        {
            reasons.Add($"DocumentContent missing columns: {string.Join(", ", missingDocumentColumns)}");
        }

        if (missingTriggers.Length > 0)
        {
            reasons.Add($"missing triggers: {string.Join(", ", missingTriggers)}");
        }

        var snapshot = new FulltextSchemaSnapshot(
            tableSql,
            ftsColumns,
            documentColumns,
            triggers,
            isContentless,
            missingTriggers.Length == 0,
            DateTimeOffset.UtcNow);

        var failureReason = reasons.Count == 0 ? null : string.Join("; ", reasons);

        return new FulltextSchemaInspectionResult(
            snapshot,
            missingFtsColumns,
            missingDocumentColumns,
            missingTriggers,
            failureReason);
    }

    private static async Task<string?> ReadTableDefinitionAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE name = $name AND type = 'table';";
        command.Parameters.AddWithValue("$name", tableName);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result as string;
    }

    private static async Task<IReadOnlyList<string>> ReadColumnsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
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

    private static async Task<IReadOnlyDictionary<string, string?>> ReadDocumentContentTriggersAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var triggers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='trigger' AND tbl_name='DocumentContent';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var sql = reader.IsDBNull(1) ? null : reader.GetString(1);
            if (!string.IsNullOrWhiteSpace(name))
            {
                triggers[name] = sql;
            }
        }

        return triggers;
    }
}

internal sealed record FulltextSchemaInspectionResult(
    FulltextSchemaSnapshot Snapshot,
    IReadOnlyList<string> MissingFtsColumns,
    IReadOnlyList<string> MissingDocumentColumns,
    IReadOnlyList<string> MissingTriggers,
    string? FailureReason)
{
    public bool IsValid => string.IsNullOrEmpty(FailureReason);
}
