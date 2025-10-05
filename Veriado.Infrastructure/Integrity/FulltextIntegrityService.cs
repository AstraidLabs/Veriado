using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Index;
using Microsoft.EntityFrameworkCore;
using Veriado.Appl.Abstractions;
using Veriado.Domain.Files;
using Veriado.Domain.Search;
using Veriado.Infrastructure.Search;

namespace Veriado.Infrastructure.Integrity;

/// <summary>
/// Implements verification and repair routines for the Lucene-based search index.
/// </summary>
internal sealed class FulltextIntegrityService : IFulltextIntegrityService
{
    private readonly IDbContextFactory<ReadOnlyDbContext> _readFactory;
    private readonly IDbContextFactory<AppDbContext> _writeFactory;
    private readonly ISearchIndexer _searchIndexer;
    private readonly LuceneIndexManager _luceneIndex;
    private readonly ILogger<FulltextIntegrityService> _logger;
    private readonly IClock _clock;
    private readonly ISearchIndexSignatureCalculator _signatureCalculator;

    public FulltextIntegrityService(
        IDbContextFactory<ReadOnlyDbContext> readFactory,
        IDbContextFactory<AppDbContext> writeFactory,
        ISearchIndexer searchIndexer,
        LuceneIndexManager luceneIndex,
        ILogger<FulltextIntegrityService> logger,
        IClock clock,
        ISearchIndexSignatureCalculator signatureCalculator)
    {
        _readFactory = readFactory ?? throw new ArgumentNullException(nameof(readFactory));
        _writeFactory = writeFactory ?? throw new ArgumentNullException(nameof(writeFactory));
        _searchIndexer = searchIndexer ?? throw new ArgumentNullException(nameof(searchIndexer));
        _luceneIndex = luceneIndex ?? throw new ArgumentNullException(nameof(luceneIndex));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _signatureCalculator = signatureCalculator ?? throw new ArgumentNullException(nameof(signatureCalculator));
    }

    public async Task<IntegrityReport> VerifyAsync(CancellationToken cancellationToken = default)
    {
        await using var readContext = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var fileIds = await readContext.Files
            .Select(f => f.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await _luceneIndex.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var indexIds = new HashSet<Guid>();
        using var reader = _luceneIndex.OpenReader();
        for (var i = 0; i < reader.NumDocs; i++)
        {
            var doc = reader.Document(i);
            if (Guid.TryParse(doc.Get(SearchFieldNames.Id), out var parsed))
            {
                indexIds.Add(parsed);
            }
        }

        var missing = fileIds.Except(indexIds).ToArray();
        var orphans = indexIds.Except(fileIds).ToArray();
        return new IntegrityReport(missing, orphans);
    }

    public async Task<int> RepairAsync(bool reindexAll, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<Guid> targetFileIds;
        IReadOnlyCollection<Guid> orphanIds;

        if (reindexAll)
        {
            await using var readContext = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            targetFileIds = await readContext.Files.Select(f => f.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            orphanIds = Array.Empty<Guid>();
        }
        else
        {
            var report = await VerifyAsync(cancellationToken).ConfigureAwait(false);
            if (report.MissingCount == 0 && report.OrphanCount == 0)
            {
                _logger.LogInformation("Lucene index already consistent");
                return 0;
            }

            targetFileIds = report.MissingFileIds;
            orphanIds = report.OrphanIndexIds;
        }

        foreach (var orphan in orphanIds)
        {
            try
            {
                await _searchIndexer.DeleteAsync(orphan, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove orphaned Lucene document {FileId}", orphan);
            }
        }

        await using var writeContext = await _writeFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var processed = 0;
        foreach (var fileId in targetFileIds)
        {
            try
            {
                if (await ReindexFileAsync(writeContext, fileId, cancellationToken).ConfigureAwait(false))
                {
                    processed++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reindex file {FileId}", fileId);
            }
        }

        await writeContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return processed;
    }

    private async Task<bool> ReindexFileAsync(AppDbContext context, Guid fileId, CancellationToken cancellationToken)
    {
        var file = await context.Files
            .Include(f => f.Content)
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            _logger.LogWarning("Skipping reindex for missing file {FileId}", fileId);
            await _searchIndexer.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var document = file.ToSearchDocument();
        await _searchIndexer.IndexAsync(document, cancellationToken).ConfigureAwait(false);

        var signature = _signatureCalculator.Compute(file);
        file.ConfirmIndexed(
            file.SearchIndex.SchemaVersion,
            UtcTimestamp.From(_clock.UtcNow),
            signature.AnalyzerVersion,
            signature.TokenHash,
            signature.NormalizedTitle);

        return true;
    }
}
