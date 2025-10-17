// VERIADO REFACTOR
namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a positive, monotonically increasing content version.
/// </summary>
public readonly record struct ContentVersion
{
    // VERIADO REFACTOR
    private ContentVersion(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Content version must be positive.");
        }

        Value = value;
    }

    // VERIADO REFACTOR
    public int Value { get; }

    // VERIADO REFACTOR
    public static ContentVersion Initial() => new(1);

    // VERIADO REFACTOR
    public ContentVersion Next()
    {
        if (Value == int.MaxValue)
        {
            throw new InvalidOperationException("Content version overflow.");
        }

        return new ContentVersion(Value + 1);
    }

    // VERIADO REFACTOR
    public static ContentVersion From(int value) => new(value);

    // VERIADO REFACTOR
    public override string ToString() => Value.ToString();
}
