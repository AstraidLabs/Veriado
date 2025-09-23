using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Integrity;

/// <summary>
/// Implements the integrity verification and repair routines for the full-text index.
/// </summary>
internal sealed class FulltextIntegrityService : IFulltextIntegrityService
{
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
        await using var readContext = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var fileIds = await readContext.Files.Select(f => f.Id).ToListAsync(cancellationToken).ConfigureAwait(false);

        var searchIndexIds = new HashSet<Guid>();
        var trigramIndexIds = new HashSet<Guid>();
        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT file_id FROM file_search_map;";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var blob = (byte[])reader[0];
                    searchIndexIds.Add(new Guid(blob));
                }
            }

            await using (var trigramCommand = connection.CreateCommand())
            {
                trigramCommand.CommandText = "SELECT file_id FROM file_trgm_map;";
                await using var reader = await trigramCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var blob = (byte[])reader[0];
                    trigramIndexIds.Add(new Guid(blob));
                }
            }
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
        var report = await VerifyAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<Guid> targetFileIds;

        if (reindexAll)
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

        await using var writeContext = await _writeFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

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
}
