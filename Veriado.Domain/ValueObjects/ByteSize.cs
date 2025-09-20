using System;

namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a non-negative size in bytes.
/// </summary>
public readonly record struct ByteSize
{
    private ByteSize(long value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the number of bytes represented by this value object.
    /// </summary>
    public long Value { get; }

    /// <summary>
    /// Creates a <see cref="ByteSize"/> from a non-negative integer value.
    /// </summary>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>The created value object.</returns>
    public static ByteSize From(long bytes)
    {
        if (bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), bytes, "Size must be non-negative.");
        }

        return new ByteSize(bytes);
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
