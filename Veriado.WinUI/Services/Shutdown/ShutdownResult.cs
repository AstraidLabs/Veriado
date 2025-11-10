namespace Veriado.WinUI.Services.Abstractions;

public readonly record struct ShutdownResult(bool IsAllowed)
{
    public static ShutdownResult Allow() => new(true);

    public static ShutdownResult Cancel() => new(false);
}
