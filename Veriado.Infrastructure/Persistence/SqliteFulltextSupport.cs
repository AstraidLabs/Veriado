namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides shared state describing the availability of SQLite full-text search features.
/// </summary>
internal static class SqliteFulltextSupport
{
    private static int _initialised;
    private static int _isAvailable;
    private static string? _failureReason;
    private static FulltextSchemaSnapshot? _schemaSnapshot;

    /// <summary>
    /// Gets a value indicating whether the required FTS5 features are available.
    /// </summary>
    public static bool IsAvailable => Volatile.Read(ref _initialised) == 1 && Volatile.Read(ref _isAvailable) == 1;

    /// <summary>
    /// Gets the last detected failure reason when FTS5 support is unavailable.
    /// </summary>
    public static string? FailureReason => Volatile.Read(ref _failureReason);

    /// <summary>
    /// Gets the last captured schema snapshot describing the FTS table configuration.
    /// </summary>
    public static FulltextSchemaSnapshot? SchemaSnapshot => Volatile.Read(ref _schemaSnapshot);

    /// <summary>
    /// Updates the cached state describing FTS5 support for the current process.
    /// </summary>
    /// <param name="available">Indicates whether FTS5 support is available.</param>
    /// <param name="failureReason">The failure reason, if any.</param>
    public static void Update(bool available, string? failureReason)
    {
        Volatile.Write(ref _isAvailable, available ? 1 : 0);
        Volatile.Write(ref _failureReason, failureReason);
        Volatile.Write(ref _initialised, 1);
    }

    /// <summary>
    /// Updates the cached schema snapshot describing the FTS configuration.
    /// </summary>
    /// <param name="snapshot">The snapshot to cache.</param>
    public static void UpdateSchemaSnapshot(FulltextSchemaSnapshot snapshot)
    {
        Volatile.Write(ref _schemaSnapshot, snapshot);
    }
}

/// <summary>
/// Represents a snapshot of the FTS schema metadata observed at runtime.
/// </summary>
/// <param name="TableSql">The raw CREATE VIRTUAL TABLE statement.</param>
/// <param name="Columns">The column names reported by PRAGMA table_info.</param>
/// <param name="Triggers">The triggers bound to DocumentContent.</param>
/// <param name="IsContentless">Indicates whether the table uses the contentless FTS5 variant.</param>
/// <param name="HasDocumentContentTriggers">Indicates whether the expected triggers are present.</param>
/// <param name="CheckedAtUtc">The timestamp when the schema was inspected.</param>
internal sealed record FulltextSchemaSnapshot(
    string? TableSql,
    IReadOnlyList<string> Columns,
    IReadOnlyDictionary<string, string?> Triggers,
    bool IsContentless,
    bool HasDocumentContentTriggers,
    DateTimeOffset CheckedAtUtc);
