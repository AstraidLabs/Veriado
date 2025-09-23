using System;
using Microsoft.UI.Xaml;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class WindowProvider : IWindowProvider
{
    private Window? _window;

    public void SetWindow(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public Window? TryGetWindow()
    {
        return _window;
    }

    public nint GetHwnd()
    {
        if (_window is null)
        {
            throw new InvalidOperationException("Window not set.");
        }

        return WinRT.Interop.WindowNative.GetWindowHandle(_window);
    }
}
