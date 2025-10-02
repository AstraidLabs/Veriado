using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.Navigation;

namespace Veriado.WinUI.Services.Abstractions;

public interface INavigationService
{
    void AttachHost(INavigationHost host);

    void Navigate(PageId pageId);
}

public interface INavigationHost
{
    Frame NavigationFrame { get; }
}
