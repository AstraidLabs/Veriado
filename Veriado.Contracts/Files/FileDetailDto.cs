using System;
using System.Collections.Generic;

namespace Veriado.Contracts.Files;

/// <summary>
/// Represents a detailed view of a file aggregate suitable for edit screens.
/// </summary>
public sealed record FileDetailDto
{
    /// <summary>
    /// Gets the unique identifier of the file.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the file name without extension.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the file extension without the leading dot.
    /// </summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>
    /// Gets the MIME type of the file.
    /// </summary>
    public string Mime { get; init; } = string.Empty;

    /// <summary>
    /// Gets the author of the file.
    /// </summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Gets the creation timestamp in UTC.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// Gets the last modification timestamp in UTC.
    /// </summary>
    public DateTimeOffset LastModifiedUtc { get; init; }

    /// <summary>
    /// Gets a value indicating whether the file is read-only.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Gets the current version of the file content.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets the file content metadata.
    /// </summary>
    public FileContentDto Content { get; init; } = new(string.Empty, 0L);

    /// <summary>
    /// Gets the file system metadata snapshot.
    /// </summary>
    public FileSystemMetadataDto SystemMetadata { get; init; } =
        new(0, DateTimeOffset.MinValue, DateTimeOffset.MinValue, DateTimeOffset.MinValue, null, null, null);

    /// <summary>
    /// Gets the extended metadata entries associated with the file.
    /// </summary>
    public IReadOnlyList<ExtendedMetadataItemDto> ExtendedMetadata { get; init; } = Array.Empty<ExtendedMetadataItemDto>();

    /// <summary>
    /// Gets the optional validity information for the document.
    /// </summary>
    public FileValidityDto? Validity { get; init; }

    /// <summary>
    /// Gets a value indicating whether the search index entry is stale.
    /// </summary>
    public bool IsIndexStale { get; init; }

    /// <summary>
    /// Gets the timestamp of the last successful indexing operation, if any.
    /// </summary>
    public DateTimeOffset? LastIndexedUtc { get; init; }

    /// <summary>
    /// Gets the indexed title stored in the search index, if known.
    /// </summary>
    public string? IndexedTitle { get; init; }

    /// <summary>
    /// Gets the schema version of the search index entry.
    /// </summary>
    public int IndexSchemaVersion { get; init; }

    /// <summary>
    /// Gets the indexed content hash if known.
    /// </summary>
    public string? IndexedContentHash { get; init; }
}
