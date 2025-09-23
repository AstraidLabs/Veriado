using Microsoft.UI.Xaml;

namespace Veriado.WinUI.Services.Windowing;

public interface IWindowProvider
{
    Window GetMainWindow();

    void Register(Window window);
}
