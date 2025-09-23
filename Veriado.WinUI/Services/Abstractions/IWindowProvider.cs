using System;
using Microsoft.UI.Xaml;

namespace Veriado.WinUI.Services.Abstractions;

public interface IWindowProvider
{
    void SetWindow(Window window);

    bool TryGetWindow(out Window? window);

    Window GetMainWindow();

    IntPtr GetHwnd(Window? window = null);

    XamlRoot GetXamlRoot(Window? window = null);

    Window GetActiveWindow();
}
