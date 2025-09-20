using System;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files;

/// <summary>
/// Represents the binary content of a file along with its derived metadata.
/// </summary>
public sealed class FileContentEntity
{
    private FileContentEntity(byte[] bytes, FileHash hash)
    {
        Bytes = bytes;
        Hash = hash;
        Length = ByteSize.From(bytes.LongLength);
    }

    /// <summary>
    /// Gets the raw file content bytes.
    /// </summary>
    public byte[] Bytes { get; }

    /// <summary>
    /// Gets the SHA-256 hash of the content.
    /// </summary>
    public FileHash Hash { get; }

    /// <summary>
    /// Gets the length of the content in bytes.
    /// </summary>
    public ByteSize Length { get; }

    /// <summary>
    /// Creates a <see cref="FileContentEntity"/> from the provided byte array.
    /// </summary>
    /// <param name="bytes">The content bytes.</param>
    /// <param name="maxBytes">Optional maximum content size constraint.</param>
    /// <returns>The created content entity.</returns>
    public static FileContentEntity FromBytes(byte[] bytes, int? maxBytes = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (maxBytes.HasValue && maxBytes.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes.Value, "Maximum bytes must be non-negative.");
        }

        if (maxBytes.HasValue && bytes.LongLength > maxBytes.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), bytes.LongLength, "Content exceeds the configured maximum size.");
        }

        var copy = new byte[bytes.Length];
        Array.Copy(bytes, copy, bytes.Length);
        var hash = FileHash.Compute(copy);
        return new FileContentEntity(copy, hash);
    }
}
