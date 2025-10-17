// VERIADO REFACTOR
namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a normalized pointer to externally stored file content.
/// </summary>
public sealed class StoragePath
{
    // VERIADO REFACTOR
    private StoragePath(string value)
    {
        Value = value;
    }

    // VERIADO REFACTOR
    public string Value { get; }

    // VERIADO REFACTOR
    public static StoragePath From(string value, int maxLength = 2048)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Storage path cannot be null or whitespace.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), normalized.Length, "Storage path exceeds the configured limit.");
        }

        return new StoragePath(normalized);
    }

    // VERIADO REFACTOR
    public override string ToString() => Value;
}
