using System;
using Microsoft.UI.Xaml;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views.Files;

public sealed partial class FilesPage : Page
{
    public FilesPage(FilesPageViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public FilesPageViewModel ViewModel { get; }

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
