namespace Veriado.WinUI.Services.Abstractions;

public interface IStatusService
{
    void ShowInfo(string message);
    void ShowError(string message);
    void Clear();
}
