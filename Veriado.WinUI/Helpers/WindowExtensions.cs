using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Veriado.WinUI.Helpers;

public static class WindowExtensions
{
    public static AppWindow? TryGetAppWindow(this Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            return AppWindow.GetFromWindowId(windowId);
        }
        catch
        {
            return null;
        }
    }
}
