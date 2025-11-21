namespace Veriado.WinUI.Views.Storage;

public sealed partial class StorageManagementPage : Page
{
    public StorageManagementPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is ViewModels.Storage.StorageManagementPageViewModel viewModel)
        {
            await viewModel.RefreshCommand.ExecuteAsync(null);
        }
    }
}
