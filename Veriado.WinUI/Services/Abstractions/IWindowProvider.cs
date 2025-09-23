namespace Veriado.WinUI.Services.Abstractions;

public interface IWindowProvider
{
    void SetWindow(Microsoft.UI.Xaml.Window window);

    Microsoft.UI.Xaml.Window? TryGetWindow();

    nint GetHwnd();
}
