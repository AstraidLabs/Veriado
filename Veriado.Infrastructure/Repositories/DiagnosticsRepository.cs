namespace Veriado.Infrastructure.Repositories;

/// <summary>
/// Provides diagnostics and statistical information about the SQLite database.
/// </summary>
internal sealed class DiagnosticsRepository : IDiagnosticsRepository
{
    private readonly InfrastructureOptions _options;
    private readonly ISearchTelemetry _telemetry;

    public DiagnosticsRepository(InfrastructureOptions options, ISearchTelemetry telemetry)
    {
        _options = options;
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    public async Task<DatabaseHealthSnapshot> GetDatabaseHealthAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);

        var journalMode = await GetScalarAsync(connection, "PRAGMA journal_mode;", cancellationToken).ConfigureAwait(false);
        var isWal = string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase);

        return new DatabaseHealthSnapshot(_options.DbPath, journalMode, isWal);
    }

    public async Task<SearchIndexSnapshot> GetIndexStatisticsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);

        var total = await GetScalarAsync(connection, "SELECT COUNT(*) FROM files;", cancellationToken).ConfigureAwait(false);
        var stale = await GetScalarAsync(connection, "SELECT COUNT(*) FROM files WHERE fts_is_stale = 1;", cancellationToken).ConfigureAwait(false);
        string? version;
        if (_options.IsFulltextAvailable)
        {
            var rawVersion = await GetScalarAsync(connection, "SELECT fts5();", cancellationToken).ConfigureAwait(false);
            version = string.IsNullOrWhiteSpace(rawVersion) ? null : rawVersion;
        }
        else
        {
            version = _options.FulltextAvailabilityError;
        }

        var totalCount = int.TryParse(total, out var parsedTotal) ? parsedTotal : 0;
        var staleCount = int.TryParse(stale, out var parsedStale) ? parsedStale : 0;
        var pageCountRaw = await GetScalarAsync(connection, "PRAGMA page_count;", cancellationToken).ConfigureAwait(false);
        var pageSizeRaw = await GetScalarAsync(connection, "PRAGMA page_size;", cancellationToken).ConfigureAwait(false);
        var pageCount = long.TryParse(pageCountRaw, out var pc) ? pc : 0;
        var pageSize = long.TryParse(pageSizeRaw, out var ps) ? ps : 0;
        var indexSizeBytes = pageCount * pageSize;

        _telemetry.UpdateIndexMetrics(totalCount, indexSizeBytes);

        return new SearchIndexSnapshot(totalCount, staleCount, version);
    }

    private static async Task<string> GetScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result?.ToString() ?? string.Empty;
    }
}
