namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents an immutable UTC timestamp value object.
/// </summary>
public readonly record struct UtcTimestamp
{
    private UtcTimestamp(DateTimeOffset value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the underlying UTC timestamp.
    /// </summary>
    public DateTimeOffset Value { get; }

    /// <summary>
    /// Creates a <see cref="UtcTimestamp"/> by converting the provided value to UTC.
    /// </summary>
    /// <param name="value">The input timestamp.</param>
    /// <returns>The created value object.</returns>
    public static UtcTimestamp From(DateTimeOffset value) => new(value.ToUniversalTime());

    /// <summary>
    /// Creates a <see cref="UtcTimestamp"/> representing the current UTC time.
    /// </summary>
    /// <returns>The created timestamp.</returns>
    public static UtcTimestamp Now() => From(DateTimeOffset.UtcNow);

    /// <summary>
    /// Converts the value object to a <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <returns>The UTC timestamp value.</returns>
    public DateTimeOffset ToDateTimeOffset() => Value;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("O");
}
