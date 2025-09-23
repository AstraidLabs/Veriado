using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Controls;
using Veriado.Contracts.Search;
using Veriado.Contracts.Search.Abstractions;
using Veriado.Services.Abstractions;
using Veriado.Services.Messages;
using Veriado.WinUI.ViewModels.Base;
using Windows.System;

namespace Veriado.WinUI.ViewModels.Search;

public sealed partial class SearchOverlayViewModel : ViewModelBase
{
    private const int SearchResultLimit = 50;
    private const int HistoryTake = 50;
    private readonly ISearchFacade _searchFacade;
    private readonly IHotStateService _hotState;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string? queryText;

    public ObservableCollection<string> Suggestions { get; } = new();

    public ObservableCollection<SearchHitDto> Results { get; } = new();

    public SearchSection<SearchFavoriteItem> Favorites { get; } = new();

    public SearchSection<SearchHistoryEntry> History { get; } = new();

    public SearchOverlayViewModel(
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler,
        ISearchFacade searchFacade,
        IHotStateService hotState)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _searchFacade = searchFacade ?? throw new ArgumentNullException(nameof(searchFacade));
        _hotState = hotState ?? throw new ArgumentNullException(nameof(hotState));

        QueryText = _hotState.LastQuery;

        Messenger.Register<OpenSearchOverlayMessage>(this, (_, _) => _ = OpenAsync());
        Messenger.Register<CloseSearchOverlayMessage>(this, (_, _) => Close());
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        IsOpen = true;
        if (string.IsNullOrWhiteSpace(QueryText))
        {
            QueryText = _hotState.LastQuery;
        }

        await SafeExecuteAsync(async ct =>
        {
            await Task.WhenAll(
                LoadFavoritesAsync(ct),
                LoadHistoryAsync(ct)).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void CloseOnEsc(VirtualKey key)
    {
        if (key == VirtualKey.Escape && string.IsNullOrWhiteSpace(QueryText))
        {
            Close();
        }
    }

    [RelayCommand]
    private void SuggestionChosen(AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args?.SelectedItem is string text)
        {
            QueryText = text;
        }
    }

    [RelayCommand]
    private async Task QuerySubmittedAsync(AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(QueryText))
        {
            return;
        }

        var query = QueryText!;
        await ExecuteSearchAsync(
            query,
            afterSearch: async ct =>
            {
                await _searchFacade.AddToHistoryAsync(query, ct).ConfigureAwait(false);
                await LoadHistoryAsync(ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    [RelayCommand]
    private void TextChanged(AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args is null || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(QueryText))
        {
            Suggestions.Clear();
            return;
        }

        var current = QueryText!;
        var lower = current.ToLowerInvariant();

        var favoriteMatches = Favorites.Items
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name) && name.Contains(lower, StringComparison.OrdinalIgnoreCase))
            .Select(name => name);

        var historyMatches = History.Items
            .Select(item => item.QueryText)
            .Where(text => !string.IsNullOrWhiteSpace(text) && text.Contains(lower, StringComparison.OrdinalIgnoreCase))
            .Select(text => text!);

        var suggestions = favoriteMatches
            .Concat(historyMatches)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        Suggestions.Clear();
        foreach (var suggestion in suggestions)
        {
            Suggestions.Add(suggestion);
        }
    }

    [RelayCommand]
    private async Task UseFavoriteAsync(SearchFavoriteItem? favorite)
    {
        if (favorite is null)
        {
            return;
        }

        QueryText = string.IsNullOrWhiteSpace(favorite.QueryText)
            ? favorite.MatchQuery
            : favorite.QueryText;

        if (string.IsNullOrWhiteSpace(QueryText))
        {
            return;
        }

        var query = QueryText!;
        var definition = new SearchFavoriteDefinition(
            favorite.Name,
            favorite.MatchQuery,
            favorite.QueryText,
            favorite.IsFuzzy);

        await ExecuteSearchAsync(
            query,
            beforeSearch: ct => _searchFacade.UseFavoriteAsync(definition, ct),
            afterSearch: LoadHistoryAsync).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task UseHistoryAsync(SearchHistoryEntry? historyItem)
    {
        if (historyItem is null)
        {
            return;
        }

        QueryText = historyItem.QueryText ?? historyItem.MatchQuery;

        if (string.IsNullOrWhiteSpace(QueryText))
        {
            return;
        }

        var query = QueryText!;
        await ExecuteSearchAsync(query, afterSearch: LoadHistoryAsync).ConfigureAwait(false);
    }

    private Task ExecuteSearchAsync(
        string query,
        Func<CancellationToken, Task>? beforeSearch = null,
        Func<CancellationToken, Task>? afterSearch = null)
    {
        return SafeExecuteAsync(async ct =>
        {
            if (beforeSearch is not null)
            {
                await beforeSearch(ct).ConfigureAwait(false);
            }

            await SearchAsync(query, ct).ConfigureAwait(false);

            if (afterSearch is not null)
            {
                await afterSearch(ct).ConfigureAwait(false);
            }
        }, "Vyhledávám…");
    }

    private async Task LoadFavoritesAsync(CancellationToken ct)
    {
        var favorites = await _searchFacade.GetFavoritesAsync(ct).ConfigureAwait(false);
        ReplaceItems(Favorites.Items, favorites);
    }

    private async Task LoadHistoryAsync(CancellationToken ct)
    {
        var history = await _searchFacade.GetHistoryAsync(HistoryTake, ct).ConfigureAwait(false);
        ReplaceItems(History.Items, history);
    }

    private async Task SearchAsync(string query, CancellationToken ct)
    {
        Results.Clear();

        var hits = await _searchFacade.SearchAsync(query, SearchResultLimit, ct).ConfigureAwait(false);
        if (hits.Count > 0)
        {
            foreach (var hit in hits)
            {
                Results.Add(hit);
            }
        }

        _hotState.LastQuery = query;

        if (Results.Count == 0)
        {
            StatusService.Info("Nebyl nalezen žádný výsledek.");
        }
        else
        {
            StatusService.Info($"Nalezeno {Results.Count} výsledků.");
        }
    }

    partial void OnQueryTextChanged(string? value)
    {
        _hotState.LastQuery = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();

        if (source.Count == 0)
        {
            return;
        }

        for (var i = 0; i < source.Count; i++)
        {
            target.Add(source[i]);
        }
    }

    public sealed class SearchSection<T>
    {
        public ObservableCollection<T> Items { get; } = new();
    }
}
