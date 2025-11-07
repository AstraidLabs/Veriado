namespace Veriado.WinUI.Services.Abstractions;

public interface IServerClock
{
    DateTimeOffset NowUtc { get; }

    DateTimeOffset NowLocal { get; }
}
