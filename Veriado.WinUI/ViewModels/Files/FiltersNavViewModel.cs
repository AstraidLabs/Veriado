using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Appl.Search.Abstractions;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.Services.Messages;
using Veriado.WinUI.ViewModels.Base;
using SavedViewDto = Veriado.Contracts.Search.SearchFavoriteItem;
using SearchHistoryItemDto = Veriado.Contracts.Search.SearchHistoryEntry;

namespace Veriado.WinUI.ViewModels.Files;

public sealed partial class FiltersNavViewModel : ViewModelBase
{
    private readonly ISearchFavoritesService _favoritesService;
    private readonly ISearchHistoryService _historyService;
    private readonly IFilesSearchSuggestionsProvider _suggestionsProvider;

    [ObservableProperty]
    private partial string? searchText;

    public ObservableCollection<string> SearchSuggestions { get; } = new();

    public ObservableCollection<SavedViewDto> Favorites { get; } = new();

    public ObservableCollection<SearchHistoryItemDto> History { get; } = new();

    public FiltersNavViewModel(
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler,
        ISearchFavoritesService favoritesService,
        ISearchHistoryService historyService,
        IFilesSearchSuggestionsProvider suggestionsProvider)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _favoritesService = favoritesService ?? throw new ArgumentNullException(nameof(favoritesService));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _suggestionsProvider = suggestionsProvider ?? throw new ArgumentNullException(nameof(suggestionsProvider));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await SafeExecuteAsync(async ct =>
        {
            await LoadFavoritesAsync(ct).ConfigureAwait(false);
            await LoadHistoryAsync(ct).ConfigureAwait(false);
            await LoadSuggestionsAsync(ct).ConfigureAwait(false);
        });
    }

    [RelayCommand]
    private void SubmitQueryFromAutoSuggest(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        SearchText = query;
        Messenger.Send(new QuerySubmittedMessage(query));
    }

    [RelayCommand]
    private void SaveCurrentQuery()
    {
        Messenger.Send(new SaveCurrentQueryRequestedMessage(SearchText));
    }

    [RelayCommand]
    private void ClearHistory()
    {
        Messenger.Send(new ClearSearchHistoryRequestedMessage());
    }

    [RelayCommand]
    private void ApplySavedView(SavedViewDto? saved)
    {
        if (saved is null)
        {
            return;
        }

        Messenger.Send(new ApplySavedViewMessage(saved));
    }

    [RelayCommand]
    private void ApplyHistoryItem(SearchHistoryItemDto? historyItem)
    {
        if (historyItem is null)
        {
            return;
        }

        var query = historyItem.QueryText ?? historyItem.MatchQuery;
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        SearchText = query;
        Messenger.Send(new QuerySubmittedMessage(query));
    }

    private async Task LoadFavoritesAsync(CancellationToken cancellationToken)
    {
        var favorites = await _favoritesService.GetAllAsync(cancellationToken).ConfigureAwait(false);

        await Dispatcher.Enqueue(() =>
        {
            Favorites.Clear();
            foreach (var favorite in favorites)
            {
                Favorites.Add(favorite);
            }
        });
    }

    private async Task LoadHistoryAsync(CancellationToken cancellationToken)
    {
        var history = await _historyService.GetRecentAsync(50, cancellationToken).ConfigureAwait(false);

        await Dispatcher.Enqueue(() =>
        {
            History.Clear();
            foreach (var item in history)
            {
                History.Add(item);
            }
        });
    }

    private async Task LoadSuggestionsAsync(CancellationToken cancellationToken)
    {
        var suggestions = await _suggestionsProvider.GetSuggestionsAsync(cancellationToken).ConfigureAwait(false);

        await Dispatcher.Enqueue(() =>
        {
            SearchSuggestions.Clear();
            foreach (var suggestion in suggestions)
            {
                SearchSuggestions.Add(suggestion);
            }
        });
    }
}
