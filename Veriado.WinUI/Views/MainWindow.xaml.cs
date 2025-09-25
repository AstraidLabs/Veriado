using System;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Veriado.WinUI.Infrastructure;
using Veriado.WinUI.Services.Messages;
using Veriado.WinUI.ViewModels;

namespace Veriado.WinUI.Views;

public sealed partial class MainWindow : Window
{
    private readonly FiltersNavPane _filtersNavPane;
    private readonly ILogger<MainWindow> _logger;
    private readonly IUiDispatcher _dispatcher;
    private readonly Throttle _textChangedThrottle;

    public MainWindow(
        ShellViewModel viewModel,
        FiltersNavPane filtersNavPane,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();

        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _filtersNavPane = filtersNavPane ?? throw new ArgumentNullException(nameof(filtersNavPane));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        LayoutRoot.DataContext = ViewModel;
        FiltersNavHost.Content = _filtersNavPane;

        _dispatcher = new UiDispatcher(DispatcherQueue);
        _textChangedThrottle = new Throttle(TimeSpan.FromMilliseconds(250));

        Closed += OnClosed;
    }

    public ShellViewModel ViewModel { get; }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        Closed -= OnClosed;
        _textChangedThrottle.Dispose();
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        WeakReferenceMessenger.Default.Send(new FocusSearchRequestedMessage());
        args.Handled = true;
    }

    private void ToggleNavigation_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.IsNavOpen = !ViewModel.IsNavOpen;
        args.Handled = true;
    }

    private void CloseNavigation_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.IsNavOpen)
        {
            ViewModel.IsNavOpen = false;
            args.Handled = true;
        }
    }

    private async void RefreshAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.Files.RefreshCommand, null, _logger)
            .ConfigureAwait(false);
        args.Handled = true;
    }

    private void ForwardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Allow child controls to handle these accelerators.
        args.Handled = false;
    }

    private void NavigationOverlay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement)
        {
            return;
        }

        if (e.OriginalSource is FrameworkElement element && IsInsideNavigationView(element))
        {
            return;
        }

        ViewModel.IsNavOpen = false;
        e.Handled = true;
    }

    private static bool IsInsideNavigationView(FrameworkElement element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is NavigationView)
            {
                return true;
            }

            current = current.Parent as FrameworkElement;
        }

        return false;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _ = _textChangedThrottle.RunAsync(async ct =>
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            await _dispatcher.RunAsync(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                CommandForwarder.TryExecute(ViewModel.Search.TextChangedCommand, args, _logger);
            }).ConfigureAwait(false);
        });
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.Search.QuerySubmittedCommand, args, _logger)
            .ConfigureAwait(false);
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        CommandForwarder.TryExecute(ViewModel.Search.SuggestionChosenCommand, args, _logger);
    }

    private async void FavoritesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.Search.UseFavoriteCommand, e.ClickedItem, _logger)
            .ConfigureAwait(false);
    }

    private async void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.Search.UseHistoryCommand, e.ClickedItem, _logger)
            .ConfigureAwait(false);
    }

    private void SearchOverlayRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        CommandForwarder.TryExecute(ViewModel.Search.CloseOnEscCommand, e.Key, _logger);
    }
}
