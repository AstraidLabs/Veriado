using System;
using System.Collections.Generic;

namespace Veriado.Contracts.Files;

/// <summary>
/// Represents the payload required to create a new file aggregate.
/// </summary>
public sealed class CreateFileRequest
{
    /// <summary>
    /// Gets or sets the file name without extension.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the file extension without the leading dot.
    /// </summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the MIME type of the file.
    /// </summary>
    public string Mime { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the author recorded for the file.
    /// </summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the binary content of the file.
    /// </summary>
    public byte[] Content { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets an optional maximum content length constraint.
    /// </summary>
    public int? MaxContentLength { get; init; }

    /// <summary>
    /// Gets or sets an optional initial file system metadata snapshot.
    /// </summary>
    public FileSystemMetadataDto? SystemMetadata { get; init; }

    /// <summary>
    /// Gets or sets optional extended metadata entries to apply after creation.
    /// </summary>
    public IReadOnlyList<ExtendedMetadataItemDto>? ExtendedMetadata { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the file should be marked read-only after creation.
    /// </summary>
    public bool IsReadOnly { get; init; }
}
