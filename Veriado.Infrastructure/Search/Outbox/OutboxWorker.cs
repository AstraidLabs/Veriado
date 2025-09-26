using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Infrastructure.Search;

namespace Veriado.Infrastructure.Search.Outbox;

/// <summary>
/// Background worker that replays committed outbox events and updates the search index.
/// </summary>
internal sealed class OutboxWorker : BackgroundService
{
    private readonly IDbContextFactory<AppDbContext> _writeFactory;
    private readonly IDbContextFactory<ReadOnlyDbContext> _readFactory;
    private readonly ISearchIndexer _searchIndexer;
    private readonly ITextExtractor _textExtractor;
    private readonly ILogger<OutboxWorker> _logger;
    private readonly InfrastructureOptions _options;
    private readonly IClock _clock;
    private readonly IFulltextIntegrityService _integrityService;

    public OutboxWorker(
        IDbContextFactory<AppDbContext> writeFactory,
        IDbContextFactory<ReadOnlyDbContext> readFactory,
        ISearchIndexer searchIndexer,
        ITextExtractor textExtractor,
        ILogger<OutboxWorker> logger,
        InfrastructureOptions options,
        IClock clock,
        IFulltextIntegrityService integrityService)
    {
        _writeFactory = writeFactory;
        _readFactory = readFactory;
        _searchIndexer = searchIndexer;
        _textExtractor = textExtractor;
        _logger = logger;
        _options = options;
        _clock = clock;
        _integrityService = integrityService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.FtsIndexingMode != FtsIndexingMode.Outbox)
        {
            return;
        }

        _logger.LogInformation("Outbox worker started");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var processedAny = await ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
                if (!processedAny)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Outbox worker stopping");
        }
    }

    private async Task<bool> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        await using var writeContext = await _writeFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var pendingEvents = await writeContext.OutboxEvents
            .AsTracking()
            .Where(evt => evt.ProcessedUtc == null)
            .OrderBy(evt => evt.CreatedUtc)
            .Take(25)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pendingEvents.Count == 0)
        {
            return false;
        }

        foreach (var outbox in pendingEvents)
        {
            var repairAttempted = false;
            while (true)
            {
                try
                {
                    await ProcessOutboxEventAsync(writeContext, outbox, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (SearchIndexCorruptedException ex)
                {
                    if (repairAttempted)
                    {
                        _logger.LogCritical(ex, "Full-text search index is corrupted. Please run the integrity repair operation before retrying outbox event {EventId}.", outbox.Id);
                        throw;
                    }

                    repairAttempted = true;
                    _logger.LogWarning(ex, "Full-text search index corruption detected while processing outbox event {EventId}. Initiating automatic repair.", outbox.Id);

                    await AttemptIntegrityRepairAsync(cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Retrying outbox event {EventId} after repairing the full-text search index.", outbox.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process outbox event {EventId}", outbox.Id);
                    break;
                }
            }
        }

        await writeContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
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

        var extractContent = true;
        if (payload.RootElement.TryGetProperty("ExtractContent", out var extractElement) && extractElement.ValueKind == JsonValueKind.False)
        {
            extractContent = false;
        }

        await ReindexFileAsync(writeContext, fileId, extractContent, cancellationToken).ConfigureAwait(false);
        outbox.ProcessedUtc = _clock.UtcNow;
    }

    private async Task AttemptIntegrityRepairAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning("Attempting automatic full rebuild of the full-text search index due to detected corruption.");
            var repaired = await _integrityService.RepairAsync(reindexAll: true, extractContent: true, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Automatic full-text index repair completed ({Repaired} entries updated).", repaired);
        }
        catch (Exception repairEx)
        {
            _logger.LogCritical(repairEx, "Automatic full-text index repair failed. Manual intervention is required.");
            throw;
        }
    }

    private async Task ReindexFileAsync(AppDbContext writeContext, Guid fileId, bool extractContent, CancellationToken cancellationToken)
    {
        await using var readContext = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var file = await readContext.Files
            .Include(f => f.Content)
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);
        if (file is null)
        {
            await _searchIndexer.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
            return;
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
        }
    }
}
