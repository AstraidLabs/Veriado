namespace Veriado.Appl.Abstractions;

/// <summary>
/// Represents the ambient search projection execution scope shared with persistence operations.
/// </summary>
public interface ISearchProjectionScope
{
    /// <summary>
    /// Ensures the underlying projection infrastructure is active and ready to accept operations.
    /// </summary>
    void EnsureActive();

    /// <summary>
    /// Executes the provided asynchronous action within the projection scope.
    /// </summary>
    /// <param name="action">The asynchronous action to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);
}
