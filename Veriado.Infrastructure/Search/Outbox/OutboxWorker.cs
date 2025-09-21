using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.Application.Abstractions;
using Veriado.Domain.Files;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Options;

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

    public OutboxWorker(
        IDbContextFactory<AppDbContext> writeFactory,
        IDbContextFactory<ReadOnlyDbContext> readFactory,
        ISearchIndexer searchIndexer,
        ITextExtractor textExtractor,
        ILogger<OutboxWorker> logger,
        InfrastructureOptions options,
        IClock clock)
    {
        _writeFactory = writeFactory;
        _readFactory = readFactory;
        _searchIndexer = searchIndexer;
        _textExtractor = textExtractor;
        _logger = logger;
        _options = options;
        _clock = clock;
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
            try
            {
                using var payload = JsonDocument.Parse(outbox.Payload);
                if (!payload.RootElement.TryGetProperty("FileId", out var fileIdElement) || !Guid.TryParse(fileIdElement.GetString(), out var fileId))
                {
                    _logger.LogWarning("Outbox event {EventId} missing file identifier", outbox.Id);
                    outbox.ProcessedUtc = _clock.UtcNow;
                    continue;
                }

                await ReindexFileAsync(writeContext, fileId, cancellationToken).ConfigureAwait(false);
                outbox.ProcessedUtc = _clock.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process outbox event {EventId}", outbox.Id);
                // Leave the event unprocessed for retry.
            }
        }

        await writeContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task ReindexFileAsync(AppDbContext writeContext, Guid fileId, CancellationToken cancellationToken)
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

        var text = await _textExtractor.ExtractTextAsync(file, cancellationToken).ConfigureAwait(false);
        var document = file.ToSearchDocument(text);
        await _searchIndexer.IndexAsync(document, cancellationToken).ConfigureAwait(false);

        var tracked = await writeContext.Files.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken).ConfigureAwait(false);
        if (tracked is not null)
        {
            tracked.ConfirmIndexed(tracked.SearchIndex.SchemaVersion, _clock.UtcNow);
        }
    }
}
