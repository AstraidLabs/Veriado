namespace Veriado.Infrastructure.Time;

/// <summary>
/// Provides access to the system UTC clock.
/// </summary>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
