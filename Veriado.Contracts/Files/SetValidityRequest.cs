namespace Veriado.Contracts.Files;

/// <summary>
/// Represents the payload required to set or update document validity information.
/// </summary>
public sealed class SetValidityRequest
{
    /// <summary>
    /// Gets or sets the identifier of the file to update.
    /// </summary>
    public Guid FileId { get; init; }

    /// <summary>
    /// Gets or sets the timestamp when the document became valid.
    /// </summary>
    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>
    /// Gets or sets the timestamp when the document expires.
    /// </summary>
    public DateTimeOffset ValidUntil { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether a physical copy exists.
    /// </summary>
    public bool HasPhysicalCopy { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether an electronic copy exists.
    /// </summary>
    public bool HasElectronicCopy { get; init; }
}
