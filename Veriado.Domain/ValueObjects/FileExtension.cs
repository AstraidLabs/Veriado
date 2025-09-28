namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents the normalized file extension without a leading dot.
/// </summary>
public readonly record struct FileExtension
{
    private const int MinLength = 1;
    private const int MaxLength = 16;

    private FileExtension(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the extension in lowercase form.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new <see cref="FileExtension"/> from the supplied extension string.
    /// </summary>
    /// <param name="value">The extension without the leading dot.</param>
    /// <returns>The created value object.</returns>
    public static FileExtension From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("File extension cannot be null or whitespace.", nameof(value));
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('.'))
        {
            throw new ArgumentException("File extension must not include a leading dot.", nameof(value));
        }

        if (trimmed.Length is < MinLength or > MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), trimmed.Length, $"File extension must be between {MinLength} and {MaxLength} characters.");
        }

        var normalized = trimmed.ToLowerInvariant();
        return new FileExtension(normalized);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
