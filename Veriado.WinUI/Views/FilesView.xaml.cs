using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views;

public sealed partial class FilesView : UserControl
{
    public FilesView()
    {
        InitializeComponent();

        var viewModel = App.Services.GetRequiredService<FilesGridViewModel>();
        DataContext = viewModel;
        viewModel.RefreshCommand.Execute(null);
    }

    public FilesGridViewModel ViewModel => (FilesGridViewModel)DataContext!;
}
