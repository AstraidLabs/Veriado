using System;
using Veriado.Domain.Primitives;

namespace Veriado.Domain.Files;

/// <summary>
/// Represents validity information associated with a file.
/// </summary>
public sealed class FileDocumentValidityEntity : EntityBase
{
    private FileDocumentValidityEntity(
        Guid fileId,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset validUntilUtc,
        bool hasPhysicalCopy,
        bool hasElectronicCopy)
        : base(fileId)
    {
        IssuedAtUtc = issuedAtUtc;
        ValidUntilUtc = validUntilUtc;
        HasPhysicalCopy = hasPhysicalCopy;
        HasElectronicCopy = hasElectronicCopy;
    }

    /// <summary>
    /// Gets the timestamp when the document was issued (UTC).
    /// </summary>
    public DateTimeOffset IssuedAtUtc { get; private set; }

    /// <summary>
    /// Gets the timestamp until which the document remains valid (UTC).
    /// </summary>
    public DateTimeOffset ValidUntilUtc { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a physical copy exists.
    /// </summary>
    public bool HasPhysicalCopy { get; private set; }

    /// <summary>
    /// Gets a value indicating whether an electronic copy exists.
    /// </summary>
    public bool HasElectronicCopy { get; private set; }

    /// <summary>
    /// Creates a new validity entity ensuring invariants.
    /// </summary>
    public static FileDocumentValidityEntity Create(
        Guid fileId,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset validUntilUtc,
        bool hasPhysicalCopy,
        bool hasElectronicCopy)
    {
        var issued = EnsureUtc(issuedAtUtc);
        var valid = EnsureUtc(validUntilUtc);
        EnsureValidPeriod(issued, valid);
        return new FileDocumentValidityEntity(fileId, issued, valid, hasPhysicalCopy, hasElectronicCopy);
    }

    /// <summary>
    /// Sets a new validity period while preserving invariants.
    /// </summary>
    public void SetPeriod(DateTimeOffset issuedAtUtc, DateTimeOffset validUntilUtc)
    {
        var issued = EnsureUtc(issuedAtUtc);
        var valid = EnsureUtc(validUntilUtc);
        EnsureValidPeriod(issued, valid);
        IssuedAtUtc = issued;
        ValidUntilUtc = valid;
    }

    /// <summary>
    /// Sets the availability of physical and electronic copies.
    /// </summary>
    public void SetCopies(bool hasPhysicalCopy, bool hasElectronicCopy)
    {
        HasPhysicalCopy = hasPhysicalCopy;
        HasElectronicCopy = hasElectronicCopy;
    }

    /// <summary>
    /// Determines whether the document is valid on the specified instant.
    /// </summary>
    public bool IsValidOn(DateTimeOffset momentUtc)
    {
        var moment = EnsureUtc(momentUtc);
        return moment >= IssuedAtUtc && moment <= ValidUntilUtc;
    }

    /// <summary>
    /// Calculates the total number of days between issuance and expiration (rounded up).
    /// </summary>
    public int DaysTotal()
    {
        var span = ValidUntilUtc - IssuedAtUtc;
        return span <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(span.TotalDays);
    }

    /// <summary>
    /// Calculates remaining days from a reference instant until expiration (rounded up).
    /// </summary>
    public int DaysRemaining(DateTimeOffset referenceUtc)
    {
        var reference = EnsureUtc(referenceUtc);

        if (reference <= IssuedAtUtc)
        {
            return DaysTotal();
        }

        if (reference >= ValidUntilUtc)
        {
            return 0;
        }

        var span = ValidUntilUtc - reference;
        return span <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(span.TotalDays);
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset timestamp) => timestamp.ToUniversalTime();

    private static void EnsureValidPeriod(DateTimeOffset issuedAtUtc, DateTimeOffset validUntilUtc)
    {
        if (validUntilUtc < issuedAtUtc)
        {
            throw new ArgumentException("Valid-until timestamp must be greater than or equal to issued-at timestamp.");
        }
    }
}
