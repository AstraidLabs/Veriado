using System;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.Contracts.Search;
using Veriado.WinUI.Infrastructure;
using Veriado.WinUI.Services.Messages;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views;

public sealed partial class FiltersNavPane : UserControl, IRecipient<FocusSearchRequestedMessage>
{
    private readonly ILogger<FiltersNavPane> _logger;
    private readonly IMessenger _messenger;

    public FiltersNavPane()
    {
        InitializeComponent();

        var services = App.Services;
        DataContext = services.GetRequiredService<FiltersNavViewModel>();
        _logger = services.GetRequiredService<ILogger<FiltersNavPane>>();
        _messenger = services.GetRequiredService<IMessenger>();

        _messenger.Register<FiltersNavPane, FocusSearchRequestedMessage>(this, static (recipient, message) => recipient.Receive(message));
    }

    public FiltersNavViewModel ViewModel => (FiltersNavViewModel)DataContext!;

    public void Receive(FocusSearchRequestedMessage message)
    {
        _ = DispatcherQueue.TryEnqueue(() => SearchBox.Focus(FocusState.Programmatic));
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.LoadCommand, null, _logger).ConfigureAwait(false);
    }

    private void Root_Unloaded(object sender, RoutedEventArgs e)
    {
        _messenger.Unregister<FocusSearchRequestedMessage>(this);
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args?.QueryText;
        await CommandForwarder.TryExecuteAsync(ViewModel.SubmitQueryFromAutoSuggestCommand, query, _logger)
            .ConfigureAwait(false);
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args?.SelectedItem is string suggestion)
        {
            ViewModel.SearchText = suggestion;
        }
    }

    private async void FavoritesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e?.ClickedItem is SearchFavoriteItem favorite)
        {
            await CommandForwarder.TryExecuteAsync(ViewModel.ApplySavedViewCommand, favorite, _logger)
                .ConfigureAwait(false);
        }
    }

    private async void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e?.ClickedItem is SearchHistoryEntry history)
        {
            await CommandForwarder.TryExecuteAsync(ViewModel.ApplyHistoryItemCommand, history, _logger)
                .ConfigureAwait(false);
        }
    }
}
