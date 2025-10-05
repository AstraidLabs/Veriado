using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views.Files;

public sealed partial class FilesPage : Page
{
    public FilesPage()
        : this(App.Services.GetRequiredService<FilesPageViewModel>())
    {
    }

    public FilesPage(FilesPageViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        InitializeComponent();
        UpdateLoadingState(ViewModel.IsBusy);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public FilesPageViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.StartHealthMonitoring();

        try
        {
            await Task.WhenAll(
                ExecuteInitialRefreshAsync(),
                ViewModel.LoadSearchSuggestionsAsync(CancellationToken.None)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to initialize {nameof(FilesPage)}: {ex}");
            ViewModel.HasError = true;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.StopHealthMonitoring();
    }

    private Task ExecuteInitialRefreshAsync()
    {
        return ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilesPageViewModel.IsBusy))
        {
            _ = DispatcherQueue.TryEnqueue(() => UpdateLoadingState(ViewModel.IsBusy));
        }
    }

    private void UpdateLoadingState(bool isBusy)
    {
        LoadingRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        LoadingRing.Opacity = isBusy ? 1d : 0d;
        ResultsHost.Opacity = isBusy ? 0d : 1d;
        ResultsHost.IsHitTestVisible = !isBusy;
        FilesScrollViewer.IsHitTestVisible = !isBusy;
    }

}
