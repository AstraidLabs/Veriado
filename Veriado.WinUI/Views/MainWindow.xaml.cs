using System;
using Microsoft.UI.Xaml;
using Veriado.WinUI.Services.Windowing;
using Veriado.WinUI.ViewModels;

namespace Veriado.WinUI.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel, IWindowProvider windowProvider)
    {
        InitializeComponent();

        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        (windowProvider ?? throw new ArgumentNullException(nameof(windowProvider))).Register(this);
    }

    public ShellViewModel ViewModel => (ShellViewModel)DataContext!;
}
