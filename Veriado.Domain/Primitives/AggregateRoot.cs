using System;

namespace Veriado.Domain.Primitives;

/// <summary>
/// Represents the base type for aggregate roots that can raise domain events.
/// </summary>
public abstract class AggregateRoot : EntityBase
{
    /// <summary>
    /// Gets the optimistic concurrency token for the aggregate root.
    /// </summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateRoot"/> class.
    /// </summary>
    /// <param name="id">The unique identifier of the aggregate root.</param>
    protected AggregateRoot(Guid id)
        : base(id)
    {
    }
}
