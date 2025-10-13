namespace Veriado.Infrastructure.Concurrency;

/// <summary>
/// Represents a queued write operation awaiting execution by the worker.
/// </summary>
internal sealed class WriteRequest
{
    private readonly Func<AppDbContext, CancellationToken, Task<object?>> _work;
    private readonly TaskCompletionSource<object?> _completion;
    private readonly CancellationToken _requestCancellation;
    private DateTimeOffset _enqueuedAt;

    public WriteRequest(
        Func<AppDbContext, CancellationToken, Task<object?>> work,
        TaskCompletionSource<object?> completion,
        IReadOnlyList<QueuedFileWrite>? trackedFiles,
        CancellationToken requestCancellation,
        Guid? partitionKey)
    {
        _work = work;
        _completion = completion;
        TrackedFiles = trackedFiles;
        _requestCancellation = requestCancellation;
        PartitionKey = partitionKey;
        _enqueuedAt = DateTimeOffset.UtcNow;
    }

    public TaskCompletionSource<object?> Completion => _completion;

    public IReadOnlyList<QueuedFileWrite>? TrackedFiles { get; }

    public Guid? PartitionKey { get; }

    public DateTimeOffset EnqueuedAt => _enqueuedAt;

    public async Task<object?> ExecuteAsync(AppDbContext context, CancellationToken workerCancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(workerCancellation, _requestCancellation);
        return await _work(context, linked.Token).ConfigureAwait(false);
    }

    internal void MarkEnqueued(DateTimeOffset timestamp)
    {
        _enqueuedAt = timestamp;
    }

    public void TrySetResult(object? value) => _completion.TrySetResult(value);

    public void TrySetException(Exception exception) => _completion.TrySetException(exception);

    public void TrySetCanceled() => _completion.TrySetCanceled();

    public void TrySetCanceled(CancellationToken cancellationToken) => _completion.TrySetCanceled(cancellationToken);
}
