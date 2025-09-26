using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Infrastructure.Search;

namespace Veriado.Infrastructure.Integrity;

/// <summary>
/// Implements the integrity verification and repair routines for the full-text index.
/// </summary>
internal sealed class FulltextIntegrityService : IFulltextIntegrityService
{
    private const string Fts5SchemaResourceName = "Veriado.Infrastructure.Persistence.Schema.Fts5.sql";

    private readonly IDbContextFactory<ReadOnlyDbContext> _readFactory;
    private readonly IDbContextFactory<AppDbContext> _writeFactory;
    private readonly ISearchIndexer _searchIndexer;
    private readonly ITextExtractor _textExtractor;
    private readonly InfrastructureOptions _options;
    private readonly ILogger<FulltextIntegrityService> _logger;
    private readonly IClock _clock;

    public FulltextIntegrityService(
        IDbContextFactory<ReadOnlyDbContext> readFactory,
        IDbContextFactory<AppDbContext> writeFactory,
        ISearchIndexer searchIndexer,
        ITextExtractor textExtractor,
        InfrastructureOptions options,
        ILogger<FulltextIntegrityService> logger,
        IClock clock)
    {
        _readFactory = readFactory;
        _writeFactory = writeFactory;
        _searchIndexer = searchIndexer;
        _textExtractor = textExtractor;
        _options = options;
        _logger = logger;
        _clock = clock;
    }

    public async Task<IntegrityReport> VerifyAsync(CancellationToken cancellationToken = default)
    {
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
        var searchMapExists = false;
        var trigramMapExists = false;
        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
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

        if (!searchMapExists || !trigramMapExists)
        {
            _logger.LogWarning("Full-text index metadata tables are missing; a rebuild will be required when repairs run.");
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

        return new IntegrityReport(missing, orphans);
    }

    public async Task<int> RepairAsync(bool reindexAll, bool extractContent, CancellationToken cancellationToken = default)
    {
        if (!_options.IsFulltextAvailable)
        {
            var reason = _options.FulltextAvailabilityError ?? "SQLite FTS5 support is unavailable.";
            _logger.LogWarning("Skipping full-text repair because FTS5 support is unavailable: {Reason}", reason);
            return 0;
        }

        IntegrityReport report;
        var requiresFullRebuild = reindexAll;

        try
        {
            report = await VerifyAsync(cancellationToken).ConfigureAwait(false);
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
            SqliteConnection.ClearAllPools();
            await RecreateFulltextSchemaAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var writeContext = await _writeFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

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
                    _logger.LogError(ex, "Failed to delete orphaned search index row for file {FileId}", orphan);
                }
            }
        }

        var processed = 0;
        foreach (var fileId in targetFileIds)
        {
            try
            {
                if (await ReindexFileAsync(writeContext, fileId, extractContent, cancellationToken).ConfigureAwait(false))
                {
                    processed++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to repair search index for file {FileId}", fileId);
            }
        }

        if (processed > 0)
        {
            await writeContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return processed;
    }

    private async Task<bool> ReindexFileAsync(AppDbContext writeContext, Guid fileId, bool extractContent, CancellationToken cancellationToken)
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

        var text = extractContent
            ? await _textExtractor.ExtractTextAsync(file, cancellationToken).ConfigureAwait(false)
            : null;
        var document = file.ToSearchDocument(text);
        await _searchIndexer.IndexAsync(document, cancellationToken).ConfigureAwait(false);

        var tracked = await writeContext.Files.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken).ConfigureAwait(false);
        if (tracked is not null)
        {
            tracked.ConfirmIndexed(tracked.SearchIndex.SchemaVersion, UtcTimestamp.From(_clock.UtcNow));
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
            "DROP TABLE IF EXISTS file_search;",
            "DROP TABLE IF EXISTS file_search_map;",
            "DROP TABLE IF EXISTS file_trgm;",
            "DROP TABLE IF EXISTS file_trgm_map;"
        };

        foreach (var statement in dropStatements)
        {
            await using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = statement;
            await dropCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var schemaSql = ReadEmbeddedSql(Fts5SchemaResourceName);
        foreach (var statement in SplitSqlStatements(schemaSql))
        {
            await using var createCommand = connection.CreateCommand();
            createCommand.CommandText = statement;
            await createCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
