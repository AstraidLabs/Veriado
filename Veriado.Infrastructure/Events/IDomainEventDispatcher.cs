using Veriado.Domain.Primitives;

namespace Veriado.Infrastructure.Events;

internal interface IDomainEventDispatcher
{
    Task DispatchAsync(AppDbContext dbContext, IReadOnlyList<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}
