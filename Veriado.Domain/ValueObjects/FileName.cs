using System;

namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a validated file name (1-255 characters, trimmed, non-empty).
/// </summary>
public readonly record struct FileName
{
    private const int MinLength = 1;
    private const int MaxLength = 255;

    private FileName(string value) => Value = value;

    /// <summary>
    /// Gets the value of the file name.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a <see cref="FileName"/> from the provided raw string.
    /// </summary>
    /// <param name="value">Raw file name value.</param>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public static FileName From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("File name cannot be null or whitespace.", nameof(value));
        }

        var trimmed = value.Trim();

        if (trimmed.Length < MinLength || trimmed.Length > MaxLength)
        {
            throw new ArgumentException($"File name must be between {MinLength} and {MaxLength} characters.", nameof(value));
        }

        return new FileName(trimmed);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
