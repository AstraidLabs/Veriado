using System;
using System.Collections.Generic;
using Veriado.Domain.Metadata;

namespace Veriado.Application.DTO;

/// <summary>
/// Represents the detailed projection of a file, including extended metadata.
/// </summary>
/// <param name="File">The core file projection.</param>
/// <param name="SystemMetadata">The file system metadata snapshot.</param>
/// <param name="ExtendedMetadata">Extended metadata values indexed by property key string.</param>
/// <param name="Title">The resolved document title.</param>
/// <param name="Subject">The resolved document subject.</param>
/// <param name="Company">The resolved company metadata value.</param>
/// <param name="Manager">The resolved manager metadata value.</param>
/// <param name="Comments">The resolved comments metadata value.</param>
public sealed record FileDetailDto(
    FileDto File,
    FileSystemMetadataDto SystemMetadata,
    IReadOnlyDictionary<string, string?> ExtendedMetadata,
    string? Title,
    string? Subject,
    string? Company,
    string? Manager,
    string? Comments);

/// <summary>
/// Represents the file system metadata snapshot for a file.
/// </summary>
/// <param name="Attributes">The file attribute flags.</param>
/// <param name="CreatedUtc">The creation timestamp.</param>
/// <param name="LastWriteUtc">The last write timestamp.</param>
/// <param name="LastAccessUtc">The last access timestamp.</param>
/// <param name="OwnerSid">The owner security identifier.</param>
/// <param name="HardLinkCount">The hard link count.</param>
/// <param name="AlternateDataStreamCount">The alternate data stream count.</param>
public sealed record FileSystemMetadataDto(
    FileAttributesFlags Attributes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastWriteUtc,
    DateTimeOffset LastAccessUtc,
    string? OwnerSid,
    uint? HardLinkCount,
    uint? AlternateDataStreamCount);
