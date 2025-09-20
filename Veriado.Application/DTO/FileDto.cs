using System;

namespace Veriado.Application.DTO;

/// <summary>
/// Represents the canonical projection of a file used by command handlers.
/// </summary>
/// <param name="Id">The file identifier.</param>
/// <param name="Name">The file name without extension.</param>
/// <param name="Extension">The file extension.</param>
/// <param name="Mime">The MIME type.</param>
/// <param name="Author">The document author.</param>
/// <param name="SizeBytes">The file size in bytes.</param>
/// <param name="Version">The file version number.</param>
/// <param name="IsReadOnly">Indicates whether the file is read-only.</param>
/// <param name="CreatedUtc">The creation timestamp.</param>
/// <param name="LastModifiedUtc">The last modification timestamp.</param>
/// <param name="Validity">Optional document validity information.</param>
public sealed record FileDto(
    Guid Id,
    string Name,
    string Extension,
    string Mime,
    string Author,
    long SizeBytes,
    int Version,
    bool IsReadOnly,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastModifiedUtc,
    FileValidityDto? Validity);

/// <summary>
/// Represents the validity state of a document.
/// </summary>
/// <param name="IssuedAtUtc">The issuance timestamp.</param>
/// <param name="ValidUntilUtc">The expiration timestamp.</param>
/// <param name="HasPhysicalCopy">Whether a physical copy exists.</param>
/// <param name="HasElectronicCopy">Whether an electronic copy exists.</param>
public sealed record FileValidityDto(
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ValidUntilUtc,
    bool HasPhysicalCopy,
    bool HasElectronicCopy);
