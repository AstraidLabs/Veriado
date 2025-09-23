using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views;

public sealed partial class FileDetailView : UserControl
{
    public FileDetailView()
    {
        InitializeComponent();

        var viewModel = App.Services.GetRequiredService<FileDetailViewModel>();
        DataContext = viewModel;
    }

    public FileDetailViewModel ViewModel => (FileDetailViewModel)DataContext!;

    private void SetReadOnlyFromToggle(object sender, RoutedEventArgs e)
    {
        ViewModel.SetReadOnlyCommand.Execute(null);
    }
}
