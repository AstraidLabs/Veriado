using System;
using System.Collections.Generic;

namespace Veriado.Domain.Primitives;

/// <summary>
/// Represents an aggregate root capable of capturing and exposing domain events.
/// </summary>
public abstract class AggregateRoot : EntityBase
{
    private readonly List<IDomainEvent> _domainEvents = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateRoot"/> class.
    /// </summary>
    /// <param name="id">Unique identifier of the aggregate.</param>
    protected AggregateRoot(Guid id)
        : base(id)
    {
    }

    /// <summary>
    /// Gets the domain events raised by the aggregate.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Clears all queued domain events.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Adds a domain event to the aggregate event queue.
    /// </summary>
    /// <param name="domainEvent">Event to raise.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="domainEvent"/> is null.</exception>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        if (domainEvent is null)
        {
            throw new ArgumentNullException(nameof(domainEvent));
        }

        _domainEvents.Add(domainEvent);
    }
}
