using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Veriado.WinUI.ViewModels;
using Veriado.WinUI.Services;

namespace Veriado;

public sealed partial class MainWindow : Window
{
    public ShellViewModel ViewModel { get; }

    private readonly INavigationService _navigationService;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Veriado";
        var services = AppHost.Services;
        ViewModel = services.GetRequiredService<ShellViewModel>();
        _navigationService = services.GetRequiredService<INavigationService>();
        DataContext = ViewModel;
        _navigationService.Initialize(RootFrame);
    }

    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException($"Navigace selhala: {e.SourcePageType}");
    }
}
