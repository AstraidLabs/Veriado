using System;

namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a non-negative size in bytes.
/// </summary>
public readonly record struct ByteSize
{
    private ByteSize(long value) => Value = value;

    /// <summary>
    /// Gets the byte size value.
    /// </summary>
    public long Value { get; }

    /// <summary>
    /// Creates a <see cref="ByteSize"/> from the provided numeric value.
    /// </summary>
    /// <param name="value">Raw byte count.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
    public static ByteSize From(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Byte size cannot be negative.");
        }

        return new ByteSize(value);
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
