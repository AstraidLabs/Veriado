using System.Globalization;

namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a positive, monotonically increasing content version.
/// </summary>
public readonly record struct ContentVersion
{
    private ContentVersion(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Content version must be positive.");
        }

        Value = value;
    }

    /// <summary>
    /// Gets the numeric value of the version.
    /// </summary>
    public int Value { get; }

    /// <summary>
    /// Gets the initial content version.
    /// </summary>
    public static ContentVersion Initial => new(1);

    /// <summary>
    /// Creates the next sequential version.
    /// </summary>
    /// <returns>The incremented version.</returns>
    public ContentVersion Next()
    {
        if (Value == int.MaxValue)
        {
            throw new InvalidOperationException("Content version overflow.");
        }

        return new ContentVersion(Value + 1);
    }

    /// <summary>
    /// Creates a new <see cref="ContentVersion"/> from the provided value.
    /// </summary>
    /// <param name="value">The numeric version value.</param>
    /// <returns>The created value object.</returns>
    public static ContentVersion From(int value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
