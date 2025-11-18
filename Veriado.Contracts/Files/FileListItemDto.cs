namespace Veriado.Contracts.Files;

/// <summary>
/// Represents a file projection optimized for list screens.
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
/// <param name="ValidUntilUtc">The optional validity expiration timestamp.</param>
public sealed record FileListItemDto(
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
    DateTimeOffset? ValidUntilUtc,
    string? PhysicalState,
    string? PhysicalStatusMessage);
