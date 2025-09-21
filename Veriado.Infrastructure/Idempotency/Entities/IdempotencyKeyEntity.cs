using System;

namespace Veriado.Infrastructure.Idempotency.Entities;

/// <summary>
/// Represents a persisted idempotency key entry.
/// </summary>
public sealed class IdempotencyKeyEntity
{
    /// <summary>
    /// Gets or sets the unique request key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when the key was created or last touched.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; set; }
}
