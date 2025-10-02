using Veriado.WinUI.Localization;
using Veriado.WinUI.ViewModels.Startup;

namespace Veriado.WinUI.Views;

public sealed partial class StartupWindow : Window
{
    public StartupWindow(StartupViewModel viewModel)
    {
        InitializeComponent();

        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        Title = LocalizedStrings.Get("StartupWindow.Title");

        if (Content is FrameworkElement contentRoot)
        {
            contentRoot.DataContext = ViewModel;
        }
    }

    public StartupViewModel ViewModel { get; }
}
