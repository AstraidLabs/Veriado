namespace Veriado.Infrastructure.Concurrency;

/// <summary>
/// Represents the contract for enqueuing write operations processed by the background write worker.
/// </summary>
internal interface IWriteQueue
{
    Task<T> EnqueueAsync<T>(
        Func<AppDbContext, CancellationToken, Task<T>> work,
        IReadOnlyList<QueuedFileWrite>? trackedFiles,
        Guid? partitionKey = null,
        CancellationToken cancellationToken = default);

    ValueTask<WriteRequest?> DequeueAsync(int partitionId, CancellationToken cancellationToken);

    bool TryDequeue(int partitionId, out WriteRequest? request);

    void Complete(Exception? error = null);
}
