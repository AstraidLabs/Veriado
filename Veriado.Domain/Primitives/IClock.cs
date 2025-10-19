namespace Veriado.Domain.Primitives;

/// <summary>
/// Provides access to the current wall-clock time in UTC for domain operations.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets the current timestamp expressed as <see cref="DateTimeOffset"/> in UTC.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
