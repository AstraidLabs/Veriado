using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views.Files;

public sealed partial class FilesPage : Page
{
    public FilesPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private FilesPageViewModel? ViewModel => DataContext as FilesPageViewModel;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ExecuteInitialRefreshAsync().ConfigureAwait(true);
    }

    private Task ExecuteInitialRefreshAsync()
    {
        return ViewModel is not null
            ? ViewModel.RefreshCommand.ExecuteAsync(null)
            : Task.CompletedTask;
    }
}
