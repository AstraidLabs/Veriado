using System.Collections.Generic;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Application.Files.Commands;

/// <summary>
/// Represents a command describing the creation of a new file aggregate.
/// </summary>
/// <param name="Name">The file name without extension.</param>
/// <param name="Extension">The file extension.</param>
/// <param name="Mime">The MIME type of the file.</param>
/// <param name="Author">The author recorded for the file.</param>
/// <param name="ContentBytes">The binary content to persist.</param>
/// <param name="MaxContentLength">An optional maximum content length constraint.</param>
/// <param name="ExtendedMetadata">Optional extended metadata patches to apply.</param>
/// <param name="SystemMetadata">Optional system metadata snapshot.</param>
/// <param name="IsReadOnly">Indicates whether the file should be marked read-only.</param>
public sealed record CreateFileCommand(
    FileName Name,
    FileExtension Extension,
    MimeType Mime,
    string Author,
    byte[] ContentBytes,
    int? MaxContentLength,
    IReadOnlyCollection<MetadataPatch> ExtendedMetadata,
    FileSystemMetadata? SystemMetadata,
    bool IsReadOnly);
