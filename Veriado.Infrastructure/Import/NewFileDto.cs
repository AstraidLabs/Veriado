using Veriado.Domain.Files;
using Veriado.Domain.Metadata;
using Veriado.Domain.Search;
using Veriado.Domain.ValueObjects;

namespace Veriado.Infrastructure.Import;

/// <summary>
/// Represents a single file entry supplied to the batch import pipeline.
/// </summary>
/// <param name="FileId">The unique identifier of the file aggregate.</param>
/// <param name="Name">The logical file name without an extension.</param>
/// <param name="Extension">The file extension.</param>
/// <param name="Mime">The MIME type associated with the file.</param>
/// <param name="Author">The document author.</param>
/// <param name="Title">The optional document title.</param>
/// <param name="FileSystemId">The identifier of the linked file-system record.</param>
/// <param name="ContentHash">The SHA-256 hash of the linked content.</param>
/// <param name="Size">The file size in bytes.</param>
/// <param name="LinkedContentVersion">The version of the linked file-system content.</param>
/// <param name="Version">The logical document version.</param>
/// <param name="CreatedUtc">The creation timestamp.</param>
/// <param name="LastModifiedUtc">The last modification timestamp.</param>
/// <param name="IsReadOnly">Indicates whether the file is read-only.</param>
/// <param name="SystemMetadata">The latest system metadata snapshot.</param>
/// <param name="Validity">Optional document validity details.</param>
/// <param name="FtsPolicy">The tokenizer policy used for indexing.</param>
/// <param name="SearchSchemaVersion">The search schema version applied to the document.</param>
/// <param name="SearchIndexedUtc">The timestamp when the document was indexed.</param>
/// <param name="SearchIndexedTitle">Optional custom indexed title override.</param>
public sealed record NewFileDto(
    Guid FileId,
    FileName Name,
    FileExtension Extension,
    MimeType Mime,
    string Author,
    string? Title,
    Guid FileSystemId,
    FileHash ContentHash,
    ByteSize Size,
    ContentVersion LinkedContentVersion,
    int Version,
    UtcTimestamp CreatedUtc,
    UtcTimestamp LastModifiedUtc,
    bool IsReadOnly,
    FileSystemMetadata SystemMetadata,
    NewFileValidityDto? Validity,
    Fts5Policy FtsPolicy,
    int SearchSchemaVersion,
    UtcTimestamp? SearchIndexedUtc,
    string? SearchIndexedTitle);

/// <summary>
/// Represents the validity payload imported for a document.
/// </summary>
/// <param name="IssuedAt">The timestamp when the validity period starts.</param>
/// <param name="ValidUntil">The timestamp when the validity period ends.</param>
/// <param name="HasPhysicalCopy">Whether a physical copy exists.</param>
/// <param name="HasElectronicCopy">Whether an electronic copy exists.</param>
public sealed record NewFileValidityDto(
    UtcTimestamp IssuedAt,
    UtcTimestamp ValidUntil,
    bool HasPhysicalCopy,
    bool HasElectronicCopy);
