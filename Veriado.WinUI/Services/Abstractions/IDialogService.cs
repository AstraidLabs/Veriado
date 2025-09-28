using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace Veriado.WinUI.Services.Abstractions;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel");
    Task ShowInfoAsync(string title, string message);
    Task ShowErrorAsync(string title, string message);
    Task ShowAsync(string title, FrameworkElement content, string primaryButtonText = "OK");
}
