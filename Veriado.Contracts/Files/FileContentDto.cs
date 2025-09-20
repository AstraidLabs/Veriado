using System;

namespace Veriado.Contracts.Files;

/// <summary>
/// Represents the binary content metadata of a file.
/// </summary>
/// <param name="Hash">The SHA-256 hash of the content in hexadecimal representation.</param>
/// <param name="Length">The length of the content in bytes.</param>
/// <param name="Bytes">The optional materialized content bytes.</param>
public sealed record FileContentDto(string Hash, long Length, byte[]? Bytes = null)
{
    /// <summary>
    /// Gets a value indicating whether the binary payload has been materialized.
    /// </summary>
    public bool HasBytes => Bytes is { Length: > 0 };

    /// <summary>
    /// Returns a copy of the DTO with content bytes cleared.
    /// </summary>
    /// <returns>The DTO without materialized bytes.</returns>
    public FileContentDto WithoutBytes() => this with { Bytes = null };

    /// <summary>
    /// Returns a copy of the DTO with the provided binary payload.
    /// </summary>
    /// <param name="payload">The content bytes to attach.</param>
    /// <returns>The DTO carrying the materialized bytes.</returns>
    public FileContentDto WithBytes(byte[]? payload)
    {
        if (payload is null)
        {
            return this with { Bytes = null };
        }

        var clone = new byte[payload.Length];
        Array.Copy(payload, clone, payload.Length);
        return this with { Bytes = clone };
    }
}
