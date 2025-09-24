using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Veriado.WinUI.Infrastructure;
using Veriado.WinUI.ViewModels;
using Windows.System;

namespace Veriado.Views;

public sealed partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly IUiDispatcher _dispatcher;
    private readonly Throttle _textChangedThrottle;

    public MainWindow(ShellViewModel viewModel, ILogger<MainWindow> logger)
    {
        InitializeComponent();

        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        LayoutRoot.DataContext = ViewModel;
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
