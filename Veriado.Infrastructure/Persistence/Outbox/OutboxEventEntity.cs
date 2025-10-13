using System;

namespace Veriado.Infrastructure.Persistence.Outbox;

/// <summary>
/// Represents a pending domain event stored for reliable delivery via the outbox pattern.
/// </summary>
public sealed class OutboxEventEntity
{
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}
