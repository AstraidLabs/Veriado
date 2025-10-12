using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
        var fileIds = await readContext.Files.Select(f => f.Id).ToListAsync(cancellationToken).ConfigureAwait(false);

        var searchIndexIds = new HashSet<Guid>();
        var trigramIndexIds = new HashSet<Guid>();
        var searchTableExists = false;
        var trigramTableExists = false;
        var searchMapExists = false;
        var trigramMapExists = false;
        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);
            searchTableExists = await TableExistsAsync(connection, "file_search", cancellationToken).ConfigureAwait(false);
            trigramTableExists = await TableExistsAsync(connection, "file_trgm", cancellationToken).ConfigureAwait(false);
            searchMapExists = await TableExistsAsync(connection, "file_search_map", cancellationToken).ConfigureAwait(false);
            trigramMapExists = await TableExistsAsync(connection, "file_trgm_map", cancellationToken).ConfigureAwait(false);

            if (searchMapExists)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT file_id FROM file_search_map;";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var blob = (byte[])reader[0];
                    searchIndexIds.Add(new Guid(blob));
                }
            }

            if (trigramMapExists)
            {
                await using var trigramCommand = connection.CreateCommand();
                trigramCommand.CommandText = "SELECT file_id FROM file_trgm_map;";
                await using var reader = await trigramCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var blob = (byte[])reader[0];
                    trigramIndexIds.Add(new Guid(blob));
                }
            }
        }

        var requiresFullRebuild = !searchTableExists
            || !trigramTableExists
            || !searchMapExists
            || !trigramMapExists;

        if (requiresFullRebuild)
        {
            var missingTables = new List<string>();
            if (!searchTableExists)
            {
                missingTables.Add("file_search");
            }

            if (!trigramTableExists)
            {
                missingTables.Add("file_trgm");
            }

            if (!searchMapExists)
            {
                missingTables.Add("file_search_map");
            }

            if (!trigramMapExists)
            {
                missingTables.Add("file_trgm_map");
            }

            _logger.LogWarning(
                "Full-text index metadata tables are missing; a rebuild will be required when repairs run. Missing: {MissingTables}",
                string.Join(", ", missingTables));
        }

        var missing = fileIds
            .Except(searchIndexIds)
            .Union(fileIds.Except(trigramIndexIds))
            .Distinct()
            .ToArray();
        var orphans = searchIndexIds
            .Except(fileIds)
            .Union(trigramIndexIds.Except(fileIds))
            .Distinct()
            .ToArray();

        verificationWatch.Stop();
        var report = new IntegrityReport(missing, orphans, requiresFullRebuild);
        _logger.LogInformation(
            "Full-text integrity verification completed in {ElapsedMs} ms (missing={MissingCount}, orphans={OrphanCount}, rebuildRequired={RebuildRequired})",
            verificationWatch.Elapsed.TotalMilliseconds,
            report.MissingCount,
            report.OrphanCount,
            report.RequiresFullRebuild);

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
            var (searchMapExists, trigramMapExists) = await GetFulltextTableStateAsync(cancellationToken).ConfigureAwait(false);
            if (!searchMapExists || !trigramMapExists)
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
            .Include(f => f.Content)
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

    private SqliteConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure options missing connection string.");
        }

        return new SqliteConnection(_options.ConnectionString);
    }

    private async Task<(bool SearchMapExists, bool TrigramMapExists)> GetFulltextTableStateAsync(CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);
        var searchMapExists = await TableExistsAsync(connection, "file_search_map", cancellationToken).ConfigureAwait(false);
        var trigramMapExists = await TableExistsAsync(connection, "file_trgm_map", cancellationToken).ConfigureAwait(false);
        return (searchMapExists, trigramMapExists);
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    private async Task RecreateFulltextSchemaAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsFulltextAvailable)
        {
            return;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Reset any outstanding WAL state before we drop and recreate the tables. A corrupted
        // or stale WAL file would otherwise continue to surface "database disk image is malformed"
        // errors even after the schema is rebuilt.
        await using (var checkpoint = connection.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await checkpoint.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var dropStatements = new[]
        {
            // Drop the virtual tables first so that any healthy shadow tables are removed with
            // them. When the database is already corrupted SQLite may fail to cascade the drop
            // and the shadow tables will need to be removed explicitly.
            "DROP TABLE IF EXISTS file_search;",
            "DROP TABLE IF EXISTS file_trgm;",
            // Explicitly drop the FTS5 shadow tables to ensure we start from a clean slate even if
            // the catalog is in an inconsistent state.
            "DROP TABLE IF EXISTS file_search_data;",
            "DROP TABLE IF EXISTS file_search_idx;",
            "DROP TABLE IF EXISTS file_search_content;",
            "DROP TABLE IF EXISTS file_search_docsize;",
            "DROP TABLE IF EXISTS file_search_config;",
            "DROP TABLE IF EXISTS file_trgm_data;",
            "DROP TABLE IF EXISTS file_trgm_idx;",
            "DROP TABLE IF EXISTS file_trgm_content;",
            "DROP TABLE IF EXISTS file_trgm_docsize;",
            "DROP TABLE IF EXISTS file_trgm_config;",
            "DROP TABLE IF EXISTS file_search_map;",
            "DROP TABLE IF EXISTS file_trgm_map;",
            "DROP TABLE IF EXISTS fts_write_ahead;",
            "DROP TABLE IF EXISTS fts_write_ahead_dlq;"
        };

        foreach (var statement in dropStatements)
        {
            await using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = statement;
            await dropCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Rebuild the database pages after removing the corrupted tables. This helps clear out any
        // lingering corrupted pages that would otherwise continue to surface "database disk image is
        // malformed" errors even after recreating the schema.
        await using (var vacuum = connection.CreateCommand())
        {
            vacuum.CommandText = "VACUUM;";
            await vacuum.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Explicitly close the connection so we can safely remove any lingering WAL/SHM files that
        // may keep corrupted pages alive even after a rebuild. Without clearing these files, SQLite
        // can continue surfacing "database disk image is malformed" errors for newly created tables.
        connection.Close();
        await DeleteWalCheckpointFilesAsync(cancellationToken).ConfigureAwait(false);

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);

        var schemaSql = ReadEmbeddedSql(Fts5SchemaResourceName);
        foreach (var statement in SplitSqlStatements(schemaSql))
        {
            await using var createCommand = connection.CreateCommand();
            createCommand.CommandText = statement;
            await createCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DeleteWalCheckpointFilesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_options.DbPath))
        {
            return;
        }

        var walPath = _options.DbPath + "-wal";
        var shmPath = _options.DbPath + "-shm";

        foreach (var path in new[] { walPath, shmPath })
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                File.Delete(path);
                _logger.LogInformation("Removed stale SQLite checkpoint file {Path} during full-text rebuild.", path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove SQLite checkpoint file {Path} during full-text rebuild.", path);
            }
        }
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

    private static IEnumerable<string> SplitSqlStatements(string script)
    {
        foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = statement.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                yield return trimmed + ';';
            }
        }
    }
}
