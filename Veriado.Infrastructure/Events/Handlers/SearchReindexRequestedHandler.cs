using Veriado.Domain.Search.Events;

namespace Veriado.Infrastructure.Events.Handlers;

internal sealed class SearchReindexRequestedHandler : IDomainEventHandler<SearchReindexRequested>
{
    private readonly ISearchIndexCoordinator _coordinator;

    public SearchReindexRequestedHandler(ISearchIndexCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public Task HandleAsync(AppDbContext dbContext, SearchReindexRequested domainEvent, CancellationToken cancellationToken)
    {
        return _coordinator.EnqueueAsync(dbContext, domainEvent.FileId, domainEvent.Reason, domainEvent.OccurredOnUtc, cancellationToken);
    }
}
