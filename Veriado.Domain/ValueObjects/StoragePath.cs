namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a normalized pointer to externally stored file content.
/// </summary>
public sealed class StoragePath : IEquatable<StoragePath>
{
    private const int MaxLength = 2048;

    private StoragePath(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the normalized storage path value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new <see cref="StoragePath"/> from the provided string value.
    /// </summary>
    /// <param name="value">The raw storage path.</param>
    /// <returns>The created value object.</returns>
    public static StoragePath From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Storage path cannot be null or whitespace.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                normalized.Length,
                $"Storage path exceeds the maximum allowed length of {MaxLength} characters.");
        }

        return new StoragePath(normalized);
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <inheritdoc />
    public bool Equals(StoragePath? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is StoragePath other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
}
