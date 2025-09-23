using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Veriado.ViewModels.Import;

namespace Veriado.Views;

public sealed partial class ImportPage : Page
{
    public ImportViewModel ViewModel { get; }

    public ImportPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<ImportViewModel>();
        DataContext = ViewModel;
    }
}
