namespace Veriado.WinUI.Services.Abstractions;

public interface IConfirmService
{
    Task<bool> TryConfirmAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        CancellationToken cancellationToken = default);
}
