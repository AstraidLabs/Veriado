namespace Veriado.Contracts.Files;

/// <summary>
/// Represents the hydrated binary payload of a file together with its core metadata.
/// </summary>
/// <param name="Id">The identifier of the file.</param>
/// <param name="Name">The file name without extension.</param>
/// <param name="Extension">The file extension without the leading dot.</param>
/// <param name="Mime">The MIME type of the file.</param>
/// <param name="Author">The author recorded for the file.</param>
/// <param name="SizeBytes">The size of the binary content in bytes.</param>
/// <param name="Version">The current version of the file content.</param>
/// <param name="IsReadOnly">Indicates whether the file is marked read-only.</param>
/// <param name="CreatedUtc">The creation timestamp in UTC.</param>
/// <param name="LastModifiedUtc">The last modification timestamp in UTC.</param>
/// <param name="Validity">Optional document validity information.</param>
/// <param name="Content">The materialized binary payload.</param>
public sealed record FileContentResponseDto(
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
    FileValidityDto? Validity,
    byte[] Content);
