using System;
using System.Globalization;

namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a validated lowercase file extension without a leading dot (1-16 characters).
/// </summary>
public readonly record struct FileExtension
{
    private const int MinLength = 1;
    private const int MaxLength = 16;

    private FileExtension(string value) => Value = value;

    /// <summary>
    /// Gets the value of the extension.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a <see cref="FileExtension"/> by validating and normalizing the provided input.
    /// </summary>
    /// <param name="value">Raw extension text.</param>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public static FileExtension From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("File extension cannot be null or whitespace.", nameof(value));
        }

        var trimmed = value.Trim();

        if (trimmed.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException("File extension must not contain dots.", nameof(value));
        }

        if (trimmed.Length < MinLength || trimmed.Length > MaxLength)
        {
            throw new ArgumentException($"File extension must be between {MinLength} and {MaxLength} characters.", nameof(value));
        }

        var normalized = trimmed.ToLower(CultureInfo.InvariantCulture);

        return new FileExtension(normalized);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
