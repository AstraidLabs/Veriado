namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents the file name without its extension.
/// </summary>
public readonly record struct FileName
{
    private const int MinLength = 1;
    private const int MaxLength = 255;

    private FileName(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the normalized name value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new <see cref="FileName"/> instance from the provided string.
    /// </summary>
    /// <param name="value">The raw file name.</param>
    /// <returns>The created value object.</returns>
    public static FileName From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("File name cannot be null or whitespace.", nameof(value));
        }

        var trimmed = value.Trim();
        if (trimmed.Length is < MinLength or > MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), trimmed.Length, $"File name must be between {MinLength} and {MaxLength} characters.");
        }

        return new FileName(trimmed);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
