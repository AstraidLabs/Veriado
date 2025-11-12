using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Common.Exceptions;
using Veriado.Appl.Search;
using Veriado.Domain.Primitives;
using Veriado.Domain.Search.Events;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.EventLog;

namespace Veriado.Infrastructure.Search;

internal sealed class SearchIndexCoordinator : ISearchIndexCoordinator
{
    private readonly ILogger<SearchIndexCoordinator> _logger;
    private readonly IDbContextFactory<AppDbContext> _writeFactory;
    private readonly IDbContextFactory<ReadOnlyDbContext> _readFactory;
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly ISearchIndexSignatureCalculator _signatureCalculator;
    private readonly ILogger<SearchProjectionService> _projectionLogger;
    private readonly IClock _clock;
    private readonly ISearchTelemetry? _telemetry;

    public SearchIndexCoordinator(
        ILogger<SearchIndexCoordinator> logger,
        IDbContextFactory<AppDbContext> writeFactory,
        IDbContextFactory<ReadOnlyDbContext> readFactory,
        IAnalyzerFactory analyzerFactory,
        ISearchIndexSignatureCalculator signatureCalculator,
        ILogger<SearchProjectionService> projectionLogger,
        IClock clock,
        ISearchTelemetry? telemetry = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _writeFactory = writeFactory ?? throw new ArgumentNullException(nameof(writeFactory));
        _readFactory = readFactory ?? throw new ArgumentNullException(nameof(readFactory));
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _signatureCalculator = signatureCalculator ?? throw new ArgumentNullException(nameof(signatureCalculator));
        _projectionLogger = projectionLogger ?? throw new ArgumentNullException(nameof(projectionLogger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _telemetry = telemetry;
    }

    public async Task EnqueueAsync(DbContext dbContext, Guid fileId, ReindexReason reason, DateTimeOffset requestedUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        if (dbContext is not AppDbContext context)
        {
            throw new ArgumentException("SearchIndexCoordinator requires AppDbContext for enqueue operations.", nameof(dbContext));
        }

        await context.ReindexQueue.AddAsync(new ReindexQueueEntry
        {
            FileId = fileId,
            Reason = reason,
            EnqueuedUtc = requestedUtc,
        }, cancellationToken).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Queued search reindex for file {FileId} due to {Reason}", fileId, reason);
        }
    }

    public async Task<SearchIndexUpdateResult> ReindexAsync(Guid fileId, ReindexReason reason, CancellationToken cancellationToken)
    {
        try
        {
            var result = await ReindexInternalAsync(fileId, cancellationToken).ConfigureAwait(false);

            switch (result.Status)
            {
                case SearchIndexUpdateStatus.Succeeded:
                    _logger.LogInformation("Reindexed file {FileId} due to {Reason}.", fileId, reason);
                    break;
                case SearchIndexUpdateStatus.NoChanges:
                    _logger.LogDebug("Search document for file {FileId} already up to date.", fileId);
                    break;
                case SearchIndexUpdateStatus.NotFound:
                    _logger.LogWarning("Skipping reindex because file {FileId} no longer exists.", fileId);
                    break;
            }

            return result;
        }
        catch (SearchIndexCorruptedException ex)
        {
            _logger.LogError(ex, "Detected search index corruption while reindexing {FileId}.", fileId);
            return SearchIndexUpdateResult.Failed(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure while reindexing {FileId}.", fileId);
            return SearchIndexUpdateResult.Failed(ex);
        }
    }

    private async Task<SearchIndexUpdateResult> ReindexInternalAsync(Guid fileId, CancellationToken cancellationToken)
    {
        await using var readContext = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var file = await readContext.Files
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            return SearchIndexUpdateResult.NotFound();
        }

        await using var writeContext = await _writeFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await writeContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var projectionScope = new SearchProjectionScopeEf(writeContext);
        var projectionService = new SearchProjectionService(writeContext, _analyzerFactory, _projectionLogger, _telemetry);

        var tracked = await writeContext.Files.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken).ConfigureAwait(false);

        if (tracked is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return SearchIndexUpdateResult.NotFound();
        }

        var signature = _signatureCalculator.Compute(tracked);
        var expectedContentHash = tracked.SearchIndex?.IndexedContentHash ?? file.SearchIndex?.IndexedContentHash;
        var expectedTokenHash = tracked.SearchIndex?.TokenHash ?? file.SearchIndex?.TokenHash;
        var newContentHash = file.ContentHash.Value;

        bool projected;
        try
        {
            projected = await projectionService
                .UpsertAsync(
                    file,
                    expectedContentHash,
                    expectedTokenHash,
                    newContentHash,
                    signature.TokenHash,
                    projectionScope,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AnalyzerOrContentDriftException)
        {
            projected = await projectionService
                .ForceReplaceAsync(
                    file,
                    newContentHash,
                    signature.TokenHash,
                    projectionScope,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!projected)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return SearchIndexUpdateResult.NoChanges();
        }

        if (tracked is not null)
        {
            tracked.ConfirmIndexed(
                tracked.SearchIndex.SchemaVersion,
                UtcTimestamp.From(_clock.UtcNow),
                signature.AnalyzerVersion,
                signature.TokenHash,
                signature.NormalizedTitle);
        }

        await writeContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return SearchIndexUpdateResult.Success(true);
    }
}
