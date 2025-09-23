using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Primitives;

namespace Veriado.Appl.Abstractions;

/// <summary>
/// Provides an abstraction for publishing domain events raised by aggregates.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes the supplied domain events to their respective handlers.
    /// </summary>
    /// <param name="events">The events to publish.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task PublishAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken cancellationToken);
}
