namespace Veriado.Appl.Abstractions;

/// <summary>
/// Provides an ambient <see cref="IRequestContext"/> using <see cref="AsyncLocal{T}"/> storage.
/// </summary>
public sealed class AmbientRequestContext : IRequestContext
{
    private sealed class Scope : IDisposable
    {
        private readonly AmbientRequestContext? _previous;
        private bool _disposed;

        public Scope(AmbientRequestContext? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _current.Value = _previous;
        }
    }

    private static readonly AsyncLocal<AmbientRequestContext?> _current = new();
    private static readonly AmbientRequestContext _default = new(null, null, null);

    private AmbientRequestContext(Guid? requestId, string? userId, string? correlationId)
    {
        RequestId = requestId;
        UserId = userId;
        CorrelationId = correlationId;
    }

    /// <inheritdoc />
    public Guid? RequestId { get; }

    /// <inheritdoc />
    public string? UserId { get; }

    /// <inheritdoc />
    public string? CorrelationId { get; }

    /// <summary>
    /// Gets the current ambient request context.
    /// </summary>
    public static AmbientRequestContext Current => _current.Value ?? _default;

    /// <summary>
    /// Begins a new ambient request context scope.
    /// </summary>
    /// <param name="requestId">The optional request identifier.</param>
    /// <param name="userId">The optional user identifier.</param>
    /// <param name="correlationId">The optional correlation identifier.</param>
    /// <returns>A disposable scope that restores the previous context when disposed.</returns>
    public static IDisposable Begin(Guid? requestId = null, string? userId = null, string? correlationId = null)
    {
        var scope = new Scope(_current.Value);
        _current.Value = new AmbientRequestContext(requestId, userId, correlationId);
        return scope;
    }

    /// <summary>
    /// Begins a new ambient request context scope using the supplied context snapshot.
    /// </summary>
    /// <param name="context">The context snapshot to apply.</param>
    /// <returns>A disposable scope that restores the previous context when disposed.</returns>
    public static IDisposable Begin(IRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Begin(context.RequestId, context.UserId, context.CorrelationId);
    }
}
