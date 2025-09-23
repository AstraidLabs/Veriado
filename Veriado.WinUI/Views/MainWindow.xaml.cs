using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Veriado.WinUI.ViewModels;

namespace Veriado.WinUI.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var viewModel = App.Services.GetRequiredService<ShellViewModel>();
        DataContext = viewModel;
    }

    public ShellViewModel ViewModel => (ShellViewModel)DataContext!;
}
