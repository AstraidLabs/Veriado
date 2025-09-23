using System;
using Microsoft.UI.Xaml;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class WindowProvider : IWindowProvider
{
    private Window? _mainWindow;

    public void SetWindow(Window window)
    {
        _mainWindow = window ?? throw new ArgumentNullException(nameof(window));
    }

    public bool TryGetWindow(out Window? window)
    {
        window = _mainWindow;
        return window is not null;
    }

    public Window GetMainWindow()
    {
        return _mainWindow
            ?? throw new InvalidOperationException("The main window has not been initialized yet.");
    }

    public IntPtr GetHwnd(Window? window = null)
    {
        var target = window ?? _mainWindow
            ?? throw new InvalidOperationException("The main window has not been initialized yet.");

        return WinRT.Interop.WindowNative.GetWindowHandle(target);
    }

    public XamlRoot GetXamlRoot(Window? window = null)
    {
        var targetWindow = window ?? _mainWindow;
        if (targetWindow is null)
        {
            targetWindow = GetMainWindow();
        }

        var xamlRoot = targetWindow.Content?.XamlRoot;
        if (xamlRoot is null)
        {
            var mainWindow = GetMainWindow();
            xamlRoot = mainWindow.Content?.XamlRoot;
        }

        return xamlRoot
            ?? throw new InvalidOperationException("The XamlRoot for the current window is not available.");
    }

    public Window GetActiveWindow()
    {
        return GetMainWindow();
    }
}
