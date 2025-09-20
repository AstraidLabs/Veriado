using System;

namespace Veriado.Application.Common.Policies;

/// <summary>
/// Defines the configuration used when importing new file content into the system.
/// </summary>
public sealed class ImportPolicy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImportPolicy"/> class.
    /// </summary>
    /// <param name="maxContentLengthBytes">The optional maximum length of file content in bytes.</param>
    public ImportPolicy(int? maxContentLengthBytes = null)
    {
        if (maxContentLengthBytes.HasValue && maxContentLengthBytes.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxContentLengthBytes), maxContentLengthBytes.Value, "Maximum content length must be greater than zero.");
        }

        MaxContentLengthBytes = maxContentLengthBytes;
    }

    /// <summary>
    /// Gets the optional maximum allowed content length in bytes.
    /// </summary>
    public int? MaxContentLengthBytes { get; }

    /// <summary>
    /// Throws when the provided length exceeds the allowed limit.
    /// </summary>
    /// <param name="length">The length to validate.</param>
    public void EnsureWithinLimit(long length)
    {
        if (MaxContentLengthBytes.HasValue && length > MaxContentLengthBytes.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "File content exceeds the configured maximum size.");
        }
    }
}
