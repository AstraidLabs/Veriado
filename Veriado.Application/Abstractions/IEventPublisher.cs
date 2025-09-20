using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Primitives;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Defines a publisher capable of dispatching domain events raised by aggregates.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes a single domain event.
    /// </summary>
    /// <param name="domainEvent">The domain event to publish.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a collection of domain events.
    /// </summary>
    /// <param name="domainEvents">The events to publish.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task PublishAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}
