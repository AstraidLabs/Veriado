namespace Veriado.WinUI.Services.Abstractions;

public interface IWindowProvider
{
    nint GetWindowHandle();
    void SetWindow(Microsoft.UI.Xaml.Window window);
}
