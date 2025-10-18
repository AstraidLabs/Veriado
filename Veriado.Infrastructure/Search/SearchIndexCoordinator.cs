using Microsoft.EntityFrameworkCore;
using Veriado.Domain.Search.Events;
using Veriado.Infrastructure.Persistence.EventLog;

namespace Veriado.Infrastructure.Search;

internal sealed class SearchIndexCoordinator : ISearchIndexCoordinator
{
    private readonly ILogger<SearchIndexCoordinator> _logger;

    public SearchIndexCoordinator(ILogger<SearchIndexCoordinator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
}
