using System;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Provides contextual information about the current request being processed by the application layer.
/// </summary>
public interface IRequestContext
{
    /// <summary>
    /// Gets the idempotency request identifier, if supplied by the caller.
    /// </summary>
    Guid? RequestId { get; }

    /// <summary>
    /// Gets the identifier of the authenticated user, if any.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Gets an optional correlation identifier that can be used for tracing.
    /// </summary>
    string? CorrelationId { get; }
}
