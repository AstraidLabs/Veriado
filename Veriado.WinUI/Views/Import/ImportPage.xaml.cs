using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Import;

namespace Veriado.WinUI.Views.Import;

public sealed partial class ImportPage : Page
{
    public ImportPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<ImportPageViewModel>();
    }
}
