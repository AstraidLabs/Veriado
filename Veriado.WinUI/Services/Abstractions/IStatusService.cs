namespace Veriado.WinUI.Services.Abstractions;

public interface IStatusService
{
    void Info(string? message);

    void Error(string? message);

    void Clear();
}
