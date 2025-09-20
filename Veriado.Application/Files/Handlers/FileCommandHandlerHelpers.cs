using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Abstractions;
using Veriado.Domain.Files;

namespace Veriado.Application.Files.Handlers;

internal static class FileCommandHandlerHelpers
{
    public static async Task PublishAndClearAsync(IEventPublisher publisher, FileEntity file, CancellationToken cancellationToken)
    {
        var events = file.DomainEvents;
        if (events.Count == 0)
        {
            return;
        }

        await publisher.PublishAsync(events, cancellationToken).ConfigureAwait(false);
        file.ClearDomainEvents();
    }
}
