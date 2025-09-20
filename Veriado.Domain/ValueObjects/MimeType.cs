using System;
using System.Globalization;

namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a MIME type value that must contain a forward slash.
/// </summary>
public readonly record struct MimeType
{
    private MimeType(string value) => Value = value;

    /// <summary>
    /// Gets the MIME type value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a <see cref="MimeType"/> after validating the input.
    /// </summary>
    /// <param name="value">Raw MIME type string.</param>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public static MimeType From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("MIME type cannot be null or whitespace.", nameof(value));
        }

        var normalized = value.Trim();
        normalized = normalized.ToLower(CultureInfo.InvariantCulture);

        if (!normalized.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("MIME type must contain a forward slash.", nameof(value));
        }

        return new MimeType(normalized);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
