namespace Veriado.Infrastructure.Time;

/// <summary>
/// Provides access to the system UTC clock.
/// </summary>
internal sealed class SystemClock : Veriado.Appl.Abstractions.IClock, Veriado.Domain.Primitives.IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
