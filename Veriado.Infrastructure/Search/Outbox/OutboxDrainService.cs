namespace Veriado.Infrastructure.Search.Outbox;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Veriado.Domain.Files;
using Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides an imperative API to drain deferred indexing events outside of the background worker loop.
/// </summary>
internal sealed class OutboxDrainService
{
    private readonly IDbContextFactory<AppDbContext> _writeFactory;
    private readonly IDbContextFactory<ReadOnlyDbContext> _readFactory;
    private readonly ISearchIndexer _searchIndexer;
    private readonly InfrastructureOptions _options;
    private readonly IClock _clock;
    private readonly IFulltextIntegrityService _integrityService;
    private readonly ILogger<OutboxDrainService> _logger;

    public OutboxDrainService(
        IDbContextFactory<AppDbContext> writeFactory,
        IDbContextFactory<ReadOnlyDbContext> readFactory,
        ISearchIndexer searchIndexer,
        InfrastructureOptions options,
        IClock clock,
        IFulltextIntegrityService integrityService,
        ILogger<OutboxDrainService> logger)
    {
        _writeFactory = writeFactory;
        _readFactory = readFactory;
        _searchIndexer = searchIndexer;
        _options = options;
        _clock = clock;
        _integrityService = integrityService;
        _logger = logger;
    }

    public async Task<int> DrainAsync(CancellationToken cancellationToken)
    {
        if (_options.FtsIndexingMode != FtsIndexingMode.Outbox)
        {
            return 0;
        }

        var pending = await LoadPendingEventIdsAsync(cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return 0;
        }

        var processed = 0;
        foreach (var eventId in pending)
        {
            if (await TryProcessAsync(eventId, cancellationToken).ConfigureAwait(false))
            {
                processed++;
            }
        }

        return processed;
    }

    private async Task<bool> TryProcessAsync(long eventId, CancellationToken cancellationToken)
    {
        var repairAttempted = false;
        while (true)
        {
            OutboxEvent? outbox = null;
            try
            {
                await using var writeContext = await _writeFactory.CreateDbContextAsync(cancellationToken)
                    .ConfigureAwait(false);
                outbox = await writeContext.OutboxEvents
                    .FirstOrDefaultAsync(evt => evt.Id == eventId, cancellationToken)
                    .ConfigureAwait(false);
                if (outbox is null)
                {
                    return false;
                }

                if (outbox.ProcessedUtc is not null)
                {
                    return false;
                }

                await ProcessOutboxEventAsync(writeContext, outbox, cancellationToken).ConfigureAwait(false);
                await writeContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (SearchIndexCorruptedException ex)
            {
                if (repairAttempted)
                {
                    _logger.LogCritical(ex, "Full-text index corruption while draining outbox event {EventId}", eventId);
                    throw;
                }

                repairAttempted = true;
                _logger.LogWarning(ex, "Corruption detected while draining outbox. Attempting repair.");
                await AttemptIntegrityRepairAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process outbox event {EventId}", eventId);
                return false;
            }
        }
    }

    private async Task<List<long>> LoadPendingEventIdsAsync(CancellationToken cancellationToken)
    {
        await using var context = await _writeFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.OutboxEvents
            .AsNoTracking()
            .Where(evt => evt.ProcessedUtc == null)
            .OrderBy(evt => evt.CreatedUtc)
            .Take(_options.OutboxBatchSize)
            .Select(evt => evt.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ProcessOutboxEventAsync(AppDbContext writeContext, OutboxEvent outbox, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(outbox.Payload);
        if (!payload.RootElement.TryGetProperty("FileId", out var fileIdElement) || !Guid.TryParse(fileIdElement.GetString(), out var fileId))
        {
            _logger.LogWarning("Outbox event {EventId} missing file identifier", outbox.Id);
            outbox.ProcessedUtc = _clock.UtcNow;
            return;
        }

        if (!_options.IsFulltextAvailable)
        {
            var tracked = await writeContext.Files.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken).ConfigureAwait(false);
            if (tracked is not null)
            {
                tracked.ConfirmIndexed(tracked.SearchIndex.SchemaVersion, UtcTimestamp.From(_clock.UtcNow));
            }

            outbox.ProcessedUtc = _clock.UtcNow;
            return;
        }

        await ReindexFileAsync(writeContext, fileId, cancellationToken).ConfigureAwait(false);
        outbox.ProcessedUtc = _clock.UtcNow;
    }

    private async Task ReindexFileAsync(AppDbContext writeContext, Guid fileId, CancellationToken cancellationToken)
    {
        FileEntity? file = null;
        try
        {
            await using var readContext = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            file = await readContext.Files
                .AsNoTracking()
                .Include(f => f.Content)
                .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SqliteException ex) when (SqliteExceptionExtensions.IndicatesMissingColumn(ex))
        {
            _logger.LogWarning(ex, "Falling back to write database when loading file {FileId}", fileId);
        }

        file ??= await writeContext.Files
            .Include(f => f.Content)
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            _logger.LogWarning("Skipping outbox event because file {FileId} no longer exists", fileId);
            await _searchIndexer.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var document = file.ToSearchDocument();
        await _searchIndexer.IndexAsync(document, cancellationToken).ConfigureAwait(false);

        var tracked = await writeContext.Files
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);
        if (tracked is not null)
        {
            tracked.ConfirmIndexed(tracked.SearchIndex.SchemaVersion, UtcTimestamp.From(_clock.UtcNow));
        }
    }

    private async Task AttemptIntegrityRepairAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning("Attempting automatic repair of full-text index before draining outbox.");
            await _integrityService.RepairAsync(reindexAll: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Automatic full-text repair failed during outbox drain.");
            throw;
        }
    }
}
