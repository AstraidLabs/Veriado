namespace Veriado.Domain.Files;

/// <summary>
/// Represents the validity information associated with a file-based document.
/// </summary>
public sealed class FileDocumentValidityEntity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileDocumentValidityEntity"/> class.
    /// </summary>
    /// <param name="issuedAt">The timestamp when the document became valid.</param>
    /// <param name="validUntil">The timestamp when the document expires.</param>
    /// <param name="hasPhysicalCopy">Whether a physical copy exists.</param>
    /// <param name="hasElectronicCopy">Whether an electronic copy exists.</param>
    public FileDocumentValidityEntity(UtcTimestamp issuedAt, UtcTimestamp validUntil, bool hasPhysicalCopy, bool hasElectronicCopy)
    {
        EnsurePeriod(issuedAt, validUntil);
        IssuedAt = issuedAt;
        ValidUntil = validUntil;
        HasPhysicalCopy = hasPhysicalCopy;
        HasElectronicCopy = hasElectronicCopy;
    }

    /// <summary>
    /// Gets the timestamp when the document becomes valid.
    /// </summary>
    public UtcTimestamp IssuedAt { get; private set; }

    /// <summary>
    /// Gets the timestamp when the document expires.
    /// </summary>
    public UtcTimestamp ValidUntil { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a physical copy exists.
    /// </summary>
    public bool HasPhysicalCopy { get; private set; }

    /// <summary>
    /// Gets a value indicating whether an electronic copy exists.
    /// </summary>
    public bool HasElectronicCopy { get; private set; }

    /// <summary>
    /// Sets the validity period, enforcing that the end is not before the start.
    /// </summary>
    /// <param name="issuedAt">The new start timestamp.</param>
    /// <param name="validUntil">The new end timestamp.</param>
    public void SetPeriod(UtcTimestamp issuedAt, UtcTimestamp validUntil)
    {
        EnsurePeriod(issuedAt, validUntil);
        IssuedAt = issuedAt;
        ValidUntil = validUntil;
    }

    /// <summary>
    /// Sets the flags indicating the presence of physical and electronic copies.
    /// </summary>
    /// <param name="hasPhysicalCopy">Whether a physical copy exists.</param>
    /// <param name="hasElectronicCopy">Whether an electronic copy exists.</param>
    public void SetCopies(bool hasPhysicalCopy, bool hasElectronicCopy)
    {
        HasPhysicalCopy = hasPhysicalCopy;
        HasElectronicCopy = hasElectronicCopy;
    }

    /// <summary>
    /// Determines whether the document is valid at the specified timestamp.
    /// </summary>
    /// <param name="moment">The timestamp to evaluate.</param>
    /// <returns><see langword="true"/> if the document is valid; otherwise <see langword="false"/>.</returns>
    public bool IsValidOn(DateTimeOffset moment)
    {
        var instant = moment.ToUniversalTime();
        return instant >= IssuedAt.Value && instant <= ValidUntil.Value;
    }

    /// <summary>
    /// Gets the total number of days in the validity period.
    /// </summary>
    /// <returns>The total number of days.</returns>
    public double DaysTotal() => (ValidUntil.Value - IssuedAt.Value).TotalDays;

    /// <summary>
    /// Gets the number of days remaining from the specified timestamp until expiration.
    /// </summary>
    /// <param name="moment">The reference timestamp.</param>
    /// <returns>The number of days remaining, or zero if already expired.</returns>
    public double DaysRemaining(DateTimeOffset moment)
    {
        var instant = moment.ToUniversalTime();
        if (instant >= ValidUntil.Value)
        {
            return 0d;
        }

        return (ValidUntil.Value - instant).TotalDays;
    }

    private static void EnsurePeriod(UtcTimestamp issuedAt, UtcTimestamp validUntil)
    {
        if (validUntil.Value < issuedAt.Value)
        {
            throw new ArgumentException("Valid-until must be greater than or equal to issued-at.", nameof(validUntil));
        }
    }
}
