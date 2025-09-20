using System;
using System.Security.Cryptography;

namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a SHA-256 hash value encoded in uppercase hexadecimal.
/// </summary>
public readonly record struct FileHash
{
    private const int ExpectedLength = 64;

    private FileHash(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the uppercase hexadecimal representation of the hash.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a <see cref="FileHash"/> from a hexadecimal string.
    /// </summary>
    /// <param name="value">The hexadecimal string.</param>
    /// <returns>The created hash value.</returns>
    public static FileHash From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Hash cannot be null or whitespace.", nameof(value));
        }

        var trimmed = value.Trim();
        if (trimmed.Length != ExpectedLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), trimmed.Length, $"Hash must be {ExpectedLength} characters long.");
        }

        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            var isHex = c is >= '0' and <= '9' or >= 'A' and <= 'F';
            if (!isHex)
            {
                throw new ArgumentException("Hash must be uppercase hexadecimal.", nameof(value));
            }
        }

        return new FileHash(trimmed);
    }

    /// <summary>
    /// Computes a SHA-256 hash for the provided binary content.
    /// </summary>
    /// <param name="bytes">The binary content.</param>
    /// <returns>The resulting hash value.</returns>
    public static FileHash Compute(ReadOnlySpan<byte> bytes)
    {
        var buffer = SHA256.HashData(bytes);
        var hex = Convert.ToHexString(buffer);
        return new FileHash(hex);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
