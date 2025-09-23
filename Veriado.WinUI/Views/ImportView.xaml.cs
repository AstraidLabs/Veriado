using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Import;

namespace Veriado.WinUI.Views;

public sealed partial class ImportView : UserControl
{
    public ImportView()
    {
        InitializeComponent();

        var viewModel = App.Services.GetRequiredService<ImportViewModel>();
        DataContext = viewModel;
    }
}
