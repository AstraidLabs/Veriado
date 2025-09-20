using System;
using System.Collections.Generic;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Application.Files.Commands;

/// <summary>
/// Represents a command describing metadata updates for a file.
/// </summary>
/// <param name="FileId">The identifier of the file to update.</param>
/// <param name="Mime">The optional new MIME type.</param>
/// <param name="Author">The optional new author value.</param>
/// <param name="IsReadOnly">An optional flag indicating whether the file should be read-only.</param>
/// <param name="SystemMetadata">An optional system metadata snapshot.</param>
/// <param name="ExtendedMetadata">Optional extended metadata patches.</param>
public sealed record UpdateMetadataCommand(
    Guid FileId,
    MimeType? Mime,
    string? Author,
    bool? IsReadOnly,
    FileSystemMetadata? SystemMetadata,
    IReadOnlyCollection<MetadataPatch> ExtendedMetadata);
