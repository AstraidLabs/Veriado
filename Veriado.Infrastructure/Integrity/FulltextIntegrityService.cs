using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Linq;
using Microsoft.Data.Sqlite;
using Veriado.Infrastructure.Search;

namespace Veriado.Infrastructure.Integrity;

/// <summary>
/// Implements the integrity verification and repair routines for the full-text index.
/// </summary>
internal sealed class FulltextIntegrityService : IFulltextIntegrityService
{
    private const string Fts5SchemaResourceName = "Veriado.Infrastructure.Persistence.Schema.Fts5.sql";
    private const int RepairBatchSize = 250;
    private const int RepairDegreeOfParallelism = 3;

    private readonly IDbContextFactory<ReadOnlyDbContext> _readFactory;
    private readonly IDbContextFactory<AppDbContext> _writeFactory;
    private readonly ISearchIndexer _searchIndexer;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly InfrastructureOptions _options;
    private readonly ILogger<FulltextIntegrityService> _logger;
    private readonly IClock _clock;
    private readonly ISearchIndexSignatureCalculator _signatureCalculator;
    private readonly ISearchTelemetry _telemetry;

    public FulltextIntegrityService(
        IDbContextFactory<ReadOnlyDbContext> readFactory,
        IDbContextFactory<AppDbContext> writeFactory,
        ISearchIndexer searchIndexer,
        ISqliteConnectionFactory connectionFactory,
        InfrastructureOptions options,
        ILogger<FulltextIntegrityService> logger,
        IClock clock,
        ISearchIndexSignatureCalculator signatureCalculator,
        ISearchTelemetry telemetry)
    {
        _readFactory = readFactory;
        _writeFactory = writeFactory;
        _searchIndexer = searchIndexer;
        _connectionFactory = connectionFactory;
        _options = options;
        _logger = logger;
        _clock = clock;
        _signatureCalculator = signatureCalculator ?? throw new ArgumentNullException(nameof(signatureCalculator));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    public async Task<IntegrityReport> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var verificationWatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting full-text integrity verification");
        if (!_options.IsFulltextAvailable)
        {
            var reason = _options.FulltextAvailabilityError ?? "SQLite FTS5 support is unavailable.";
            _logger.LogWarning("Skipping full-text integrity verification because FTS5 support is unavailable: {Reason}", reason);
            return new IntegrityReport(Array.Empty<Guid>(), Array.Empty<Guid>());
        }

        await using var readContext = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var batchSize = _options.IntegrityBatchSize > 0 ? _options.IntegrityBatchSize : 2000;
        var timeSliceMs = _options.IntegrityTimeSliceMs;
        var maxMemoryBytes = GC.GetTotalMemory(forceFullCollection: false);
        var missing = new List<Guid>();
        var orphans = new List<Guid>();
        var timedOut = false;
        var searchTableExists = false;
        var contentTableExists = false;

        await using (var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false))
        {
            var connection = lease.Connection;
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            searchTableExists = await TableExistsAsync(connection, "file_search", cancellationToken).ConfigureAwait(false);
            contentTableExists = await TableExistsAsync(connection, "DocumentContent", cancellationToken).ConfigureAwait(false);

            if (contentTableExists && searchTableExists)
            {
                (timedOut, maxMemoryBytes) = await CollectMissingIdsAsync(
                        connection,
                        batchSize,
                        missing,
                        verificationWatch,
                        timeSliceMs,
                        cancellationToken,
                        maxMemoryBytes)
                    .ConfigureAwait(false);

                if (!timedOut)
                {
                    (timedOut, maxMemoryBytes) = await CollectOrphanIdsAsync(
                            connection,
                            batchSize,
                            orphans,
                            verificationWatch,
                            timeSliceMs,
                            cancellationToken,
                            maxMemoryBytes)
                        .ConfigureAwait(false);
                }
            }
        }

        var requiresFullRebuild = !searchTableExists || !contentTableExists;

        if (requiresFullRebuild)
        {
            var missingTables = new List<string>();
            if (!searchTableExists)
            {
                missingTables.Add("file_search");
            }

            if (!contentTableExists)
            {
                missingTables.Add("DocumentContent");
            }

            _logger.LogWarning(
                "Full-text index metadata tables are missing; a rebuild will be required when repairs run. Missing: {MissingTables}",
                string.Join(", ", missingTables));

            await foreach (var fileId in readContext.Files
                .AsNoTracking()
                .Select(file => file.Id)
                .AsAsyncEnumerable()
                .WithCancellation(cancellationToken))
            {
                missing.Add(fileId);
            }

            maxMemoryBytes = Math.Max(maxMemoryBytes, GC.GetTotalMemory(forceFullCollection: false));
        }

        verificationWatch.Stop();
        maxMemoryBytes = Math.Max(maxMemoryBytes, GC.GetTotalMemory(forceFullCollection: false));
        var peakMemoryMb = maxMemoryBytes / (1024d * 1024d);

        var report = new IntegrityReport(missing, orphans, requiresFullRebuild);
        _logger.LogInformation(
            "Full-text integrity verification completed in {ElapsedMs} ms (missing={MissingCount}, orphans={OrphanCount}, rebuildRequired={RebuildRequired}, timedOut={TimedOut}, peakMemoryMb={PeakMemoryMb:F2})",
            verificationWatch.Elapsed.TotalMilliseconds,
            report.MissingCount,
            report.OrphanCount,
            report.RequiresFullRebuild,
            timedOut,
            peakMemoryMb);

        return report;
    }

    public async Task<int> RepairAsync(bool reindexAll, CancellationToken cancellationToken = default)
    {
        if (!_options.IsFulltextAvailable)
        {
            var reason = _options.FulltextAvailabilityError ?? "SQLite FTS5 support is unavailable.";
            _logger.LogWarning("Skipping full-text repair because FTS5 support is unavailable: {Reason}", reason);
            return 0;
        }

        _logger.LogInformation("Starting full-text repair (reindexAll={ReindexAll})", reindexAll);
        IntegrityReport report;
        var requiresFullRebuild = reindexAll;

        try
        {
            report = await VerifyAsync(cancellationToken).ConfigureAwait(false);
            requiresFullRebuild |= report.RequiresFullRebuild;
        }
        catch (SqliteException ex)
        {
            requiresFullRebuild = true;
            var message = ex.IndicatesDatabaseCorruption()
                ? "Full-text index metadata is corrupted; forcing full rebuild before repair."
                : "Full-text index metadata could not be read; forcing full rebuild before repair.";
            _logger.LogWarning(ex, message);
            report = new IntegrityReport(Array.Empty<Guid>(), Array.Empty<Guid>());
        }

        IReadOnlyCollection<Guid> targetFileIds;

        if (!requiresFullRebuild)
        {
            var contentTableExists = await GetFulltextTableStateAsync(cancellationToken).ConfigureAwait(false);
            if (!contentTableExists)
            {
                requiresFullRebuild = true;
                _logger.LogInformation("Full-text metadata tables missing; forcing full rebuild before repair.");
            }
        }

        if (requiresFullRebuild)
        {
            await using var readContext = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            targetFileIds = await readContext.Files.Select(f => f.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (report.MissingCount == 0 && report.OrphanCount == 0)
            {
                _logger.LogInformation("Full-text index already consistent");
                return 0;
            }

            targetFileIds = report.MissingFileIds;
        }

        if (requiresFullRebuild)
        {
            // Ensure we are working with a clean connection pool before we attempt to
            // rebuild the virtual tables. Otherwise pooled connections may continue to
            // serve corrupted pages and cause the rebuild to fail.
            await _connectionFactory.ResetAsync(cancellationToken).ConfigureAwait(false);
            SqliteConnection.ClearAllPools();
            await RecreateFulltextSchemaAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!requiresFullRebuild)
        {
            foreach (var orphan in report.OrphanIndexIds)
            {
                try
                {
                    await _searchIndexer.DeleteAsync(orphan, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _telemetry.RecordRepairFailure();
                    _logger.LogError(ex, "Failed to delete orphaned search index row for file {FileId}", orphan);
                }
            }
        }

        var targetFileIdsArray = targetFileIds.ToArray();
        if (targetFileIdsArray.Length == 0)
        {
            return 0;
        }

        var batches = targetFileIdsArray.Chunk(RepairBatchSize).ToArray();
        using var semaphore = new SemaphoreSlim(RepairDegreeOfParallelism);
        var tasks = new List<Task<int>>(batches.Length);

        foreach (var batch in batches)
        {
            tasks.Add(ProcessBatchAsync(batch, semaphore, cancellationToken));
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var repaired = results.Sum();
        _logger.LogInformation(
            "Full-text repair finished: batches={BatchCount}, processed={ProcessedCount}",
            batches.Length,
            repaired);
        return repaired;

        async Task<int> ProcessBatchAsync(Guid[] batch, SemaphoreSlim limiter, CancellationToken ct)
        {
            await limiter.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                await using var writeContext = await _writeFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                await using var transaction = await writeContext.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var transactionId = Guid.NewGuid();
                _logger.LogInformation(
                    "Repair transaction {TransactionId} started for batch size {BatchSize}",
                    transactionId,
                    batch.Length);

                var processedCount = 0;

                foreach (var fileId in batch)
                {
                    try
                    {
                        if (await ReindexFileAsync(writeContext, fileId, ct).ConfigureAwait(false))
                        {
                            processedCount++;
                        }
                    }
                    catch (SearchIndexCorruptedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _telemetry.RecordRepairFailure();
                        _logger.LogError(ex, "Failed to repair search index for file {FileId}", fileId);
                    }
                }

                try
                {
                    await writeContext.SaveChangesAsync(ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Repair transaction {TransactionId} committed ({ProcessedCount} files updated)",
                        transactionId,
                        processedCount);
                }
                catch
                {
                    try
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.LogError(
                            rollbackEx,
                            "Failed to rollback repair transaction {TransactionId} after commit error",
                            transactionId);
                    }

                    _logger.LogWarning(
                        "Repair transaction {TransactionId} rolled back due to failure",
                        transactionId);
                    throw;
                }

                _telemetry.RecordRepairBatch(batch.Length);

                return processedCount;
            }
            catch (SearchIndexCorruptedException)
            {
                _telemetry.RecordRepairFailure();
                throw;
            }
            catch (Exception ex)
            {
                _telemetry.RecordRepairFailure();
                _logger.LogError(ex, "Failed to commit repair batch of size {BatchSize}", batch.Length);
                throw;
            }
            finally
            {
                limiter.Release();
            }
        }
    }

    private async Task<bool> ReindexFileAsync(AppDbContext writeContext, Guid fileId, CancellationToken cancellationToken)
    {
        await using var readContext = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var file = await readContext.Files
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);
        if (file is null)
        {
            return false;
        }

        var document = file.ToSearchDocument();
        await _searchIndexer.IndexAsync(document, cancellationToken).ConfigureAwait(false);

        var tracked = await writeContext.Files.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken).ConfigureAwait(false);
        if (tracked is not null)
        {
            var signature = _signatureCalculator.Compute(tracked);
            tracked.ConfirmIndexed(
                tracked.SearchIndex.SchemaVersion,
                UtcTimestamp.From(_clock.UtcNow),
                signature.AnalyzerVersion,
                signature.TokenHash,
                signature.NormalizedTitle);
            return true;
        }

        return false;
    }

    private async Task<bool> GetFulltextTableStateAsync(CancellationToken cancellationToken)
    {
        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var contentTableExists = await TableExistsAsync(connection, "DocumentContent", cancellationToken).ConfigureAwait(false);
        return contentTableExists;
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    private async Task<(bool TimedOut, long MaxMemoryBytes)> CollectMissingIdsAsync(
        SqliteConnection connection,
        int batchSize,
        List<Guid> destination,
        Stopwatch stopwatch,
        int timeSliceMs,
        CancellationToken cancellationToken,
        long maxMemoryBytes)
    {
        var lastRowId = 0L;
        var currentMaxMemoryBytes = maxMemoryBytes;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT f.rowid, f.id
FROM files f
WHERE NOT EXISTS (SELECT 1 FROM DocumentContent dc WHERE dc.file_id = f.id)
  AND f.rowid > $lastRowId
ORDER BY f.rowid
LIMIT $batchSize;";
            command.Parameters.Add("$lastRowId", SqliteType.Integer).Value = lastRowId;
            command.Parameters.Add("$batchSize", SqliteType.Integer).Value = batchSize;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var fetched = 0;
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lastRowId = reader.GetInt64(0);
                var blob = (byte[])reader[1];
                destination.Add(new Guid(blob));
                fetched++;
            }

            if (fetched == 0)
            {
                return (false, currentMaxMemoryBytes);
            }

            currentMaxMemoryBytes = Math.Max(currentMaxMemoryBytes, GC.GetTotalMemory(forceFullCollection: false));
            _logger.LogDebug(
                "Integrity verification queued {BatchCount} missing candidates (total={Total})",
                fetched,
                destination.Count);

            if (timeSliceMs > 0 && stopwatch.ElapsedMilliseconds > timeSliceMs)
            {
                _logger.LogWarning(
                    "Integrity verification paused after collecting {Missing} missing ids in {ElapsedMs} ms.",
                    destination.Count,
                    stopwatch.Elapsed.TotalMilliseconds);
                return (true, currentMaxMemoryBytes);
            }
        }
    }

    private async Task<(bool TimedOut, long MaxMemoryBytes)> CollectOrphanIdsAsync(
        SqliteConnection connection,
        int batchSize,
        List<Guid> destination,
        Stopwatch stopwatch,
        int timeSliceMs,
        CancellationToken cancellationToken,
        long maxMemoryBytes)
    {
        var lastRowId = 0L;
        var currentMaxMemoryBytes = maxMemoryBytes;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT dc.rowid, dc.file_id
FROM DocumentContent dc
LEFT JOIN files f ON f.id = dc.file_id
WHERE f.id IS NULL
  AND dc.rowid > $lastRowId
ORDER BY dc.rowid
LIMIT $batchSize;";
            command.Parameters.Add("$lastRowId", SqliteType.Integer).Value = lastRowId;
            command.Parameters.Add("$batchSize", SqliteType.Integer).Value = batchSize;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var fetched = 0;
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lastRowId = reader.GetInt64(0);
                var blob = (byte[])reader[1];
                destination.Add(new Guid(blob));
                fetched++;
            }

            if (fetched == 0)
            {
                return (false, currentMaxMemoryBytes);
            }

            currentMaxMemoryBytes = Math.Max(currentMaxMemoryBytes, GC.GetTotalMemory(forceFullCollection: false));
            _logger.LogDebug(
                "Integrity verification queued {BatchCount} orphan candidates (total={Total})",
                fetched,
                destination.Count);

            if (timeSliceMs > 0 && stopwatch.ElapsedMilliseconds > timeSliceMs)
            {
                _logger.LogWarning(
                    "Integrity verification paused after collecting {Orphans} orphan ids in {ElapsedMs} ms.",
                    destination.Count,
                    stopwatch.Elapsed.TotalMilliseconds);
                return (true, currentMaxMemoryBytes);
            }
        }
    }

    private async Task RecreateFulltextSchemaAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsFulltextAvailable)
        {
            return;
        }

        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Reset any outstanding WAL state before we drop and recreate the tables. A corrupted
        // or stale WAL file would otherwise continue to surface "database disk image is malformed"
        // errors even after the schema is rebuilt.
        await ExecutePragmaAsync(connection, "wal_checkpoint(TRUNCATE)", cancellationToken).ConfigureAwait(false);

        var dropStatements = new[]
        {
            "DROP TRIGGER IF EXISTS dc_ai;",
            "DROP TRIGGER IF EXISTS dc_au;",
            "DROP TRIGGER IF EXISTS dc_ad;",
            "DROP TABLE IF EXISTS file_search;",
            "DROP TABLE IF EXISTS file_search_data;",
            "DROP TABLE IF EXISTS file_search_idx;",
            "DROP TABLE IF EXISTS file_search_content;",
            "DROP TABLE IF EXISTS file_search_docsize;",
            "DROP TABLE IF EXISTS file_search_config;",
            "DROP TABLE IF EXISTS file_trgm;",
            "DROP TABLE IF EXISTS DocumentContent;",
            "DROP TABLE IF EXISTS fts_write_ahead;",
            "DROP TABLE IF EXISTS fts_write_ahead_dlq;"
        };

        await ExecuteStatementsAsync(connection, dropStatements, "drop", cancellationToken).ConfigureAwait(false);

        // Rebuild the database pages after removing the corrupted tables. This helps clear out any
        // lingering corrupted pages that would otherwise continue to surface "database disk image is
        // malformed" errors even after recreating the schema.
        await using (var vacuum = connection.CreateCommand())
        {
            vacuum.CommandText = "VACUUM;";
            await vacuum.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Take an explicit checkpoint after VACUUM to truncate any WAL pages produced by the rebuild
        // before creating the new schema objects.
        await ExecutePragmaAsync(connection, "wal_checkpoint(TRUNCATE)", cancellationToken).ConfigureAwait(false);

        var schemaSql = ReadEmbeddedSql(Fts5SchemaResourceName);
        var schemaStatements = SplitSqlStatements(schemaSql).ToArray();
        await ExecuteStatementsAsync(connection, schemaStatements, "create", cancellationToken).ConfigureAwait(false);

        await using (var rebuildCommand = connection.CreateCommand())
        {
            rebuildCommand.CommandText = "INSERT INTO file_search(file_search) VALUES('rebuild');";
            LogSchemaStatement("rebuild", 1, 1, rebuildCommand.CommandText);
            await rebuildCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Full-text schema recreated successfully.");
    }

    private static string ReadEmbeddedSql(string resourceName)
    {
        var assembly = typeof(FulltextIntegrityService).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            var availableResources = string.Join(", ", assembly.GetManifestResourceNames());
            throw new FileNotFoundException($"Embedded SQL resource '{resourceName}' was not found. Available resources: {availableResources}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task ExecuteStatementsAsync(
        SqliteConnection connection,
        IReadOnlyList<string> statements,
        string phase,
        CancellationToken cancellationToken)
    {
        if (statements.Count == 0)
        {
            return;
        }

        for (var i = 0; i < statements.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var statement = statements[i];
            LogSchemaStatement(phase, i + 1, statements.Count, statement);

            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void LogSchemaStatement(string phase, int index, int total, string statement)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var normalized = statement.Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        var length = normalized.Length;
        var tailLength = Math.Min(100, length);
        var tail = normalized.Substring(length - tailLength, tailLength);

        _logger.LogDebug(
            "Executing full-text schema statement {Phase} {Index}/{Total} (length={Length}, tail={Tail}):\n{Statement}",
            phase,
            index,
            total,
            length,
            tail,
            normalized);
    }

    private async Task ExecutePragmaAsync(
        SqliteConnection connection,
        string pragma,
        CancellationToken cancellationToken)
    {
        await using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = $"PRAGMA {pragma};";
        LogSchemaStatement("pragma", 1, 1, pragmaCommand.CommandText);
        await pragmaCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<string> SplitSqlStatements(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            yield break;
        }

        var builder = new StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inBracketIdentifier = false;
        var inLineComment = false;
        var inBlockComment = false;
        var blockDepth = 0;

        for (var i = 0; i < script.Length; i++)
        {
            var current = script[i];
            var next = i + 1 < script.Length ? script[i + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && !inBracketIdentifier)
            {
                if (current == '-' && next == '-')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (current == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            builder.Append(current);

            if (!inDoubleQuote && !inBracketIdentifier && current == '\'')
            {
                if (inSingleQuote && next == '\'')
                {
                    builder.Append(next);
                    i++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (!inSingleQuote && !inBracketIdentifier && current == '"')
            {
                if (inDoubleQuote && next == '"')
                {
                    builder.Append(next);
                    i++;
                    continue;
                }

                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote)
            {
                if (current == '[')
                {
                    inBracketIdentifier = true;
                    continue;
                }

                if (current == ']')
                {
                    inBracketIdentifier = false;
                    continue;
                }
            }

            if (inSingleQuote || inDoubleQuote || inBracketIdentifier)
            {
                continue;
            }

            if (char.IsLetter(current))
            {
                var tokenStart = i;
                while (i + 1 < script.Length && char.IsLetter(script[i + 1]))
                {
                    i++;
                    builder.Append(script[i]);
                }

                var token = script[tokenStart..(i + 1)];
                if (string.Equals(token, "BEGIN", StringComparison.OrdinalIgnoreCase))
                {
                    blockDepth++;
                }
                else if (string.Equals(token, "END", StringComparison.OrdinalIgnoreCase) && blockDepth > 0)
                {
                    blockDepth--;
                }

                continue;
            }

            if (current == ';' && blockDepth == 0)
            {
                var statement = builder.ToString().Trim();
                if (statement.Length > 0)
                {
                    yield return statement;
                }

                builder.Clear();
            }
        }

        var trailing = builder.ToString().Trim();
        if (trailing.Length > 0)
        {
            yield return trailing;
        }
    }
}
