using System;

namespace Veriado.Domain.Metadata;

/// <summary>
/// Represents a property key composed of a format identifier and property identifier.
/// </summary>
public readonly record struct PropertyKey
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyKey"/> struct.
    /// </summary>
    /// <param name="formatId">The property set format identifier.</param>
    /// <param name="propertyId">The property identifier within the set.</param>
    public PropertyKey(Guid formatId, int propertyId)
    {
        if (formatId == Guid.Empty)
        {
            throw new ArgumentException("Format identifier must be a non-empty GUID.", nameof(formatId));
        }

        FormatId = formatId;
        PropertyId = propertyId;
    }

    /// <summary>
    /// Gets the property set format identifier.
    /// </summary>
    public Guid FormatId { get; }

    /// <summary>
    /// Gets the property identifier within the property set.
    /// </summary>
    public int PropertyId { get; }

    /// <summary>
    /// Creates a <see cref="PropertyKey"/> from the provided components.
    /// </summary>
    /// <param name="formatId">The format identifier.</param>
    /// <param name="propertyId">The property identifier.</param>
    /// <returns>The created property key.</returns>
    public static PropertyKey From(Guid formatId, int propertyId) => new(formatId, propertyId);

    /// <inheritdoc />
    public override string ToString() => $"{FormatId:D}/{PropertyId}";
}
