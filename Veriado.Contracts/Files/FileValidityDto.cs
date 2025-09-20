using System;

namespace Veriado.Contracts.Files;

/// <summary>
/// Represents the document validity window and related copy information.
/// </summary>
/// <param name="IssuedAt">The timestamp when the document became valid.</param>
/// <param name="ValidUntil">The timestamp when the document expires.</param>
/// <param name="HasPhysicalCopy">Indicates whether a physical copy exists.</param>
/// <param name="HasElectronicCopy">Indicates whether a digital copy exists.</param>
public sealed record FileValidityDto(
    DateTimeOffset IssuedAt,
    DateTimeOffset ValidUntil,
    bool HasPhysicalCopy,
    bool HasElectronicCopy)
{
    /// <summary>
    /// Gets the total number of days in the validity range.
    /// </summary>
    public double DaysTotal => (ValidUntil - IssuedAt).TotalDays;
}
