using System;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files;

/// <summary>
/// Represents the binary content of a file along with its SHA-256 hash.
/// </summary>
public sealed class FileContentEntity : EntityBase
{
    private byte[] _data;

    private FileContentEntity(Guid fileId, byte[] data, FileHash hash)
        : base(fileId)
    {
        _data = data;
        Hash = hash;
        Size = ByteSize.From(data.LongLength);
    }

    /// <summary>
    /// Gets the binary content of the file.
    /// </summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>
    /// Gets the SHA-256 hash of the content.
    /// </summary>
    public FileHash Hash { get; private set; }

    /// <summary>
    /// Gets the size of the content in bytes.
    /// </summary>
    public ByteSize Size { get; private set; }

    /// <summary>
    /// Creates a <see cref="FileContentEntity"/> from raw bytes.
    /// </summary>
    /// <param name="fileId">Identifier of the owning file aggregate.</param>
    /// <param name="bytes">Binary content.</param>
    /// <param name="maxBytes">Optional maximum allowed size.</param>
    public static FileContentEntity FromBytes(Guid fileId, ReadOnlySpan<byte> bytes, int? maxBytes = null)
    {
        ValidateLength(bytes, maxBytes);

        var dataCopy = bytes.ToArray();
        var hash = FileHash.Compute(dataCopy);
        return new FileContentEntity(fileId, dataCopy, hash);
    }

    /// <summary>
    /// Replaces the existing content with new bytes.
    /// </summary>
    /// <param name="bytes">New binary content.</param>
    /// <param name="maxBytes">Optional maximum allowed size.</param>
    public void ReplaceWith(ReadOnlySpan<byte> bytes, int? maxBytes = null)
    {
        ValidateLength(bytes, maxBytes);

        var dataCopy = bytes.ToArray();
        var hash = FileHash.Compute(dataCopy);
        _data = dataCopy;
        Hash = hash;
        Size = ByteSize.From(dataCopy.LongLength);
    }

    private static void ValidateLength(ReadOnlySpan<byte> bytes, int? maxBytes)
    {
        if (maxBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "Maximum bytes cannot be negative.");
        }

        if (maxBytes.HasValue && bytes.Length > maxBytes.Value)
        {
            throw new ArgumentException("File content exceeds the allowed size limit.", nameof(bytes));
        }
    }
}
