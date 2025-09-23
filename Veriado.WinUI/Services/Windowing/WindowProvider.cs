using System;
using Microsoft.UI.Xaml;

namespace Veriado.WinUI.Services.Windowing;

public sealed class WindowProvider : IWindowProvider
{
    private Window? _window;

    public Window GetMainWindow()
    {
        return _window ?? throw new InvalidOperationException("Main window has not been registered.");
    }

    public void Register(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }
}
