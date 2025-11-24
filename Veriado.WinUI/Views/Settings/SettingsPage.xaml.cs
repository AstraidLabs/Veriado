namespace Veriado.WinUI.Views.Settings;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is ViewModels.Settings.SettingsPageViewModel viewModel)
        {
            await viewModel.LoadStorageSettingsCommand.ExecuteAsync(null);
        }
    }
}
