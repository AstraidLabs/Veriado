namespace Veriado.Infrastructure.Persistence.EventLog;

/// <summary>
/// Represents a serialized domain event captured alongside the write transaction.
/// </summary>
public sealed class DomainEventLogEntry
{
    public long Id { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string EventJson { get; set; } = string.Empty;

    public string AggregateId { get; set; } = string.Empty;

    public DateTimeOffset OccurredUtc { get; set; }

    public DateTimeOffset? ProcessedUtc { get; set; }

    public int RetryCount { get; set; }
}

