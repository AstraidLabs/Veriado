using Veriado.WinUI.Localization;

namespace Veriado.WinUI.Views;

public sealed partial class StartupWindow : Window
{
    public StartupWindow()
    {
        InitializeComponent();

        Title = LocalizedStrings.Get("StartupWindow.Title");
    }
}
