using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels;

namespace Veriado.WinUI.Views;

public sealed partial class ImportPage : Page
{
    public ImportPage()
    {
        InitializeComponent();
        ViewModel = AppHost.Services.GetRequiredService<ImportViewModel>();
        DataContext = ViewModel;
    }

    public ImportViewModel ViewModel { get; }

    public ImportViewModel VM => ViewModel;
}
