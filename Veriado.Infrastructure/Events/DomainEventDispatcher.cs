using System.Reflection;
using Veriado.Domain.Primitives;

namespace Veriado.Infrastructure.Events;

internal sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DomainEventDispatcher> _logger;

    public DomainEventDispatcher(IServiceScopeFactory scopeFactory, ILogger<DomainEventDispatcher> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task DispatchAsync(AppDbContext dbContext, IReadOnlyList<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        if (domainEvents.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;

        foreach (var domainEvent in domainEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var handlerEnumerableType = typeof(IEnumerable<>).MakeGenericType(typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType()));
            if (provider.GetService(handlerEnumerableType) is not IEnumerable<object> handlers)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("No handlers registered for domain event {EventType}", domainEvent.GetType().FullName);
                }

                continue;
            }

            foreach (var handler in handlers)
            {
                await InvokeHandlerAsync(handler, dbContext, domainEvent, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Task InvokeHandlerAsync(object handler, AppDbContext context, IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var method = typeof(DomainEventDispatcher)
            .GetMethod(nameof(InvokeHandlerCore), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(domainEvent.GetType());

        return (Task)method.Invoke(null, new[] { handler, context, domainEvent, cancellationToken })!;
    }

    private static Task InvokeHandlerCore<TEvent>(
        IDomainEventHandler<TEvent> handler,
        AppDbContext context,
        TEvent domainEvent,
        CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        return handler.HandleAsync(context, domainEvent, cancellationToken);
    }
}
