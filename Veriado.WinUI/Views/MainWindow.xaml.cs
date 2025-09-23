using System;
using Microsoft.UI.Xaml;
using Veriado.WinUI.ViewModels;

namespace Veriado.WinUI.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public ShellViewModel ViewModel => (ShellViewModel)DataContext!;
}
