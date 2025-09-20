using System;

namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a MIME type value.
/// </summary>
public readonly record struct MimeType
{
    private MimeType(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the canonical MIME type string.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a MIME type value object from the provided string.
    /// </summary>
    /// <param name="value">The MIME type string.</param>
    /// <returns>The created value object.</returns>
    public static MimeType From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("MIME type cannot be null or whitespace.", nameof(value));
        }

        var trimmed = value.Trim();
        if (!trimmed.Contains('/'))
        {
            throw new ArgumentException("MIME type must contain a slash separator.", nameof(value));
        }

        return new MimeType(trimmed);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
