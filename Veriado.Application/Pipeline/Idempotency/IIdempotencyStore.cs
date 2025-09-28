namespace Veriado.Appl.Pipeline.Idempotency;

/// <summary>
/// Provides persistence for idempotency request processing.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to register a request identifier for processing.
    /// </summary>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the request may proceed; otherwise <see langword="false"/>.</returns>
    Task<bool> TryRegisterAsync(Guid requestId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a previously registered request as successfully processed.
    /// </summary>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task MarkProcessedAsync(Guid requestId, CancellationToken cancellationToken);

    /// <summary>
    /// Releases a previously registered request after a failure, allowing retries.
    /// </summary>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task MarkFailedAsync(Guid requestId, CancellationToken cancellationToken);
}
