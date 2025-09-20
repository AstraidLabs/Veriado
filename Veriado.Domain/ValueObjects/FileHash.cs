using System;
using System.Security.Cryptography;

namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a SHA-256 hash encoded as 64 uppercase hexadecimal characters.
/// </summary>
public readonly record struct FileHash
{
    private const int HashLength = 64;

    private FileHash(string value) => Value = value;

    /// <summary>
    /// Gets the hash value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a <see cref="FileHash"/> from a precomputed hexadecimal representation.
    /// </summary>
    /// <param name="value">Hexadecimal hash string.</param>
    /// <exception cref="ArgumentException">Thrown when the input is not a valid SHA-256 hash.</exception>
    public static FileHash From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Hash cannot be null or whitespace.", nameof(value));
        }

        var normalized = value.Trim();

        if (normalized.Length != HashLength)
        {
            throw new ArgumentException($"Hash must be {HashLength} characters long.", nameof(value));
        }

        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            var isHex = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
            if (!isHex)
            {
                throw new ArgumentException("Hash must contain only uppercase hexadecimal characters.", nameof(value));
            }
        }

        return new FileHash(normalized);
    }

    /// <summary>
    /// Computes the SHA-256 hash for the provided binary data.
    /// </summary>
    /// <param name="data">Binary data to hash.</param>
    public static FileHash Compute(ReadOnlySpan<byte> data)
    {
        Span<byte> hashBytes = stackalloc byte[32];
        if (!SHA256.TryComputeHash(data, hashBytes, out _))
        {
            throw new InvalidOperationException("Failed to compute SHA-256 hash.");
        }

        Span<char> chars = stackalloc char[HashLength];
        var position = 0;
        for (var i = 0; i < hashBytes.Length; i++)
        {
            var b = hashBytes[i];
            chars[position++] = GetHexValue((int)(b >> 4));
            chars[position++] = GetHexValue((int)(b & 0xF));
        }

        return new FileHash(new string(chars));
    }

    private static char GetHexValue(int value) => value < 10
        ? (char)('0' + value)
        : (char)('A' + (value - 10));

    /// <inheritdoc />
    public override string ToString() => Value;
}
