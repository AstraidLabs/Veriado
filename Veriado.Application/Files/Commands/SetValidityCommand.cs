using System;
using Veriado.Domain.ValueObjects;

namespace Veriado.Application.Files.Commands;

/// <summary>
/// Represents a command describing the desired validity settings for a file.
/// </summary>
/// <param name="FileId">The identifier of the file to update.</param>
/// <param name="IssuedAt">The timestamp when the document becomes valid.</param>
/// <param name="ValidUntil">The timestamp when the document expires.</param>
/// <param name="HasPhysicalCopy">Indicates whether a physical copy exists.</param>
/// <param name="HasElectronicCopy">Indicates whether an electronic copy exists.</param>
public sealed record SetValidityCommand(
    Guid FileId,
    UtcTimestamp IssuedAt,
    UtcTimestamp ValidUntil,
    bool HasPhysicalCopy,
    bool HasElectronicCopy);
