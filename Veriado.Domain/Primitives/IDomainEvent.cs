using System;

namespace Veriado.Domain.Primitives;

/// <summary>
/// Represents a domain event emitted by aggregate roots and entities.
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// Gets the unique identifier of the event.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// Gets the UTC timestamp when the event occurred.
    /// </summary>
    DateTimeOffset OccurredOnUtc { get; }
}
