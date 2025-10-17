using Veriado.Domain.Search.Events;

namespace Veriado.Infrastructure.Persistence.EventLog;

/// <summary>
/// Represents a queued search reindex request captured within the primary transaction.
/// </summary>
public sealed class ReindexQueueEntry
{
    public long Id { get; set; }

    public Guid FileId { get; set; }

    public ReindexReason Reason { get; set; }

    public DateTimeOffset EnqueuedUtc { get; set; }

    public DateTimeOffset? ProcessedUtc { get; set; }

    public int RetryCount { get; set; }
}

