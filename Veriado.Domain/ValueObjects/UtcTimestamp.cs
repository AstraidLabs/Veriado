using System;

namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a timestamp guaranteed to be in UTC.
/// </summary>
public readonly record struct UtcTimestamp
{
    private UtcTimestamp(DateTimeOffset value) => Value = value;

    /// <summary>
    /// Gets the UTC timestamp value.
    /// </summary>
    public DateTimeOffset Value { get; }

    /// <summary>
    /// Creates a <see cref="UtcTimestamp"/> from a <see cref="DateTimeOffset"/>, converting to UTC when necessary.
    /// </summary>
    /// <param name="value">Source timestamp.</param>
    public static UtcTimestamp From(DateTimeOffset value)
    {
        var utcValue = value.ToUniversalTime();
        return new UtcTimestamp(utcValue);
    }

    /// <summary>
    /// Creates a <see cref="UtcTimestamp"/> representing the current instant.
    /// </summary>
    public static UtcTimestamp Now() => new(DateTimeOffset.UtcNow);

    /// <inheritdoc />
    public override string ToString() => Value.ToString("O");
}
