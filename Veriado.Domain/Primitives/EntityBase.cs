using System;

namespace Veriado.Domain.Primitives;

/// <summary>
/// Base type for domain entities enforcing identity equality through a non-empty <see cref="Guid"/> identifier.
/// </summary>
public abstract class EntityBase : IEquatable<EntityBase>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityBase"/> class.
    /// </summary>
    /// <param name="id">Unique identifier of the entity.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is empty.</exception>
    protected EntityBase(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Entity identifier cannot be empty.", nameof(id));
        }

        Id = id;
    }

    /// <summary>
    /// Gets the unique identifier of the entity.
    /// </summary>
    public Guid Id { get; }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EntityBase other && Equals(other);

    /// <inheritdoc />
    public bool Equals(EntityBase? other) => other is not null && Id.Equals(other.Id);

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>
    /// Equality operator comparing entity identity.
    /// </summary>
    public static bool operator ==(EntityBase? left, EntityBase? right) => Equals(left, right);

    /// <summary>
    /// Inequality operator comparing entity identity.
    /// </summary>
    public static bool operator !=(EntityBase? left, EntityBase? right) => !Equals(left, right);
}
