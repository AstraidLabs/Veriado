using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views.Files;

public sealed partial class FilesPage : Page
{
    public FilesPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<FilesPageViewModel>();
        Loaded += OnLoaded;
    }

    private FilesPageViewModel ViewModel => (FilesPageViewModel)DataContext;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ExecuteInitialRefreshAsync().ConfigureAwait(true);
    }

    private Task ExecuteInitialRefreshAsync()
    {
        return ViewModel.RefreshCommand.ExecuteAsync(null);
    }
}
