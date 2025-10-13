using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Processes reindex requests produced by integrity checks and audits.
/// </summary>
internal sealed class IndexQueue : IIndexQueue, IAsyncDisposable
{
    private readonly Channel<IndexDocument> _channel;
    private readonly IDbContextFactory<ReadOnlyDbContext> _readFactory;
    private readonly IDbContextFactory<AppDbContext> _writeFactory;
    private readonly ISearchIndexer _searchIndexer;
    private readonly ISearchIndexSignatureCalculator _signatureCalculator;
    private readonly IClock _clock;
    private readonly ILogger<IndexQueue> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processorTask;

    public IndexQueue(
        IDbContextFactory<ReadOnlyDbContext> readFactory,
        IDbContextFactory<AppDbContext> writeFactory,
        ISearchIndexer searchIndexer,
        ISearchIndexSignatureCalculator signatureCalculator,
        IClock clock,
        ILogger<IndexQueue> logger)
    {
        _readFactory = readFactory ?? throw new ArgumentNullException(nameof(readFactory));
        _writeFactory = writeFactory ?? throw new ArgumentNullException(nameof(writeFactory));
        _searchIndexer = searchIndexer ?? throw new ArgumentNullException(nameof(searchIndexer));
        _signatureCalculator = signatureCalculator
            ?? throw new ArgumentNullException(nameof(signatureCalculator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _channel = Channel.CreateUnbounded<IndexDocument>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        _processorTask = Task.Run(ProcessQueueAsync);
    }

    public void Enqueue(IndexDocument document)
    {
        if (!_channel.Writer.TryWrite(document))
        {
            _logger.LogWarning("Index queue is unavailable; failed to enqueue {FileId} for reindex.", document.FileId);
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var document in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    await ProcessDocumentAsync(document, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Reindex operation for {FileId} failed.", document.FileId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the application shuts down.
        }
    }

    private async Task ProcessDocumentAsync(IndexDocument document, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(document.FileId, out var fileId))
        {
            _logger.LogWarning("Skipping reindex for invalid file identifier {FileId}.", document.FileId);
            return;
        }

        await using var readContext = await _readFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var file = await readContext.Files
            .Include(f => f.Content)
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);
        if (file is null)
        {
            _logger.LogDebug("File {FileId} not found during reindex.", fileId);
            return;
        }

        await _searchIndexer.IndexAsync(file.ToSearchDocument(), cancellationToken).ConfigureAwait(false);

        await using var writeContext = await _writeFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var tracked = await writeContext.Files
            .Include(f => f.SearchIndex)
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);
        if (tracked is null)
        {
            _logger.LogDebug("File {FileId} not available in write context after reindex.", fileId);
            return;
        }

        var signature = _signatureCalculator.Compute(tracked);
        tracked.ConfirmIndexed(
            tracked.SearchIndex?.SchemaVersion ?? 1,
            UtcTimestamp.From(_clock.UtcNow),
            signature.AnalyzerVersion,
            signature.TokenHash,
            signature.NormalizedTitle);
        await writeContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Reindexed file {FileId} via index queue.", fileId);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try
        {
            await _processorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
