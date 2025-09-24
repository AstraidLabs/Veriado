using System;
using Microsoft.UI.Xaml;
using Veriado.WinUI.ViewModels.Startup;

namespace Veriado.WinUI.Views;

public sealed partial class StartupWindow : Window
{
    public StartupWindow(StartupViewModel viewModel)
    {
        InitializeComponent();

        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        if (Content is FrameworkElement contentRoot)
        {
            contentRoot.DataContext = ViewModel;
        }
    }

    public StartupViewModel ViewModel { get; }
}
