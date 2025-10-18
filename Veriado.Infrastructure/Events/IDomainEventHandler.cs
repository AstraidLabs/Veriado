using Veriado.Domain.Primitives;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Events;

internal interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(AppDbContext dbContext, TEvent domainEvent, CancellationToken cancellationToken);
}
