using Microsoft.EntityFrameworkCore;
using Veriado.Domain.Search.Events;

namespace Veriado.Appl.Abstractions;

public interface ISearchIndexCoordinator
{
    Task EnqueueAsync(DbContext dbContext, Guid fileId, ReindexReason reason, DateTimeOffset requestedUtc, CancellationToken cancellationToken);
}
