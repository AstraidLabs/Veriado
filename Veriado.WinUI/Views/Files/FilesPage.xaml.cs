using System;
using Microsoft.UI.Xaml;
using Veriado.Contracts.Files;
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
        Unloaded += OnUnloaded;
    }

    public FilesPageViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ViewModel.StartHealthMonitoring();
        await ExecuteInitialRefreshAsync().ConfigureAwait(true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.StopHealthMonitoring();
    }

    private Task ExecuteInitialRefreshAsync()
    {
        return ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private void OnOpenDetailClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileSummaryDto dto)
        {
            if (ViewModel.OpenDetailCommand.CanExecute(dto))
            {
                ViewModel.OpenDetailCommand.Execute(dto);
            }
        }
    }
}
