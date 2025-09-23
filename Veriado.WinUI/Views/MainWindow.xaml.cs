using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Veriado.Services;
using Veriado.ViewModels.Shell;

namespace Veriado.Views;

public sealed partial class MainWindow : Window
{
    public ShellViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        Title = "Veriado";

        var services = App.Current.Services;
        ViewModel = services.GetRequiredService<ShellViewModel>();
        DataContext = ViewModel;

        var navigationService = services.GetRequiredService<INavigationService>();
        navigationService.Initialize(ContentFrame);
    }
}
