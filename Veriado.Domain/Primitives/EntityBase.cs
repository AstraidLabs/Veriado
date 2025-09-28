namespace Veriado.Domain.Primitives;

/// <summary>
/// Provides the base functionality shared by all entities, including domain event tracking.
/// </summary>
public abstract class EntityBase
{
    private readonly List<IDomainEvent> _domainEvents = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityBase"/> class.
    /// </summary>
    /// <param name="id">The unique identifier of the entity.</param>
    protected EntityBase(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Identifier must be a non-empty GUID.", nameof(id));
        }

        Id = id;
    }

    /// <summary>
    /// Gets the unique identifier of the entity.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the domain events that have been raised by this entity.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Clears all domain events emitted by this entity.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Raises a domain event produced by the entity.
    /// </summary>
    /// <param name="domainEvent">The event to record.</param>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
}
