using System;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Concurrency;

/// <summary>
/// Represents the contract for enqueuing write operations processed by the background write worker.
/// </summary>
internal interface IWriteQueue
{
    Task<T> EnqueueAsync<T>(Func<AppDbContext, CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);

    ValueTask<WriteRequest?> DequeueAsync(CancellationToken cancellationToken);
}
