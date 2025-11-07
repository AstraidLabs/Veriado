using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class ServerClock : IServerClock
{
    public DateTimeOffset NowUtc => DateTimeOffset.UtcNow;

    public DateTimeOffset NowLocal => NowUtc.ToLocalTime();
}
