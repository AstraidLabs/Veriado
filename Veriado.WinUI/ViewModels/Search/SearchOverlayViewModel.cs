using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Veriado.Application.Search.Abstractions;
using Veriado.Contracts.Search;
using Veriado.WinUI.ViewModels.Base;
using Veriado.WinUI.Models.Search;

namespace Veriado.WinUI.ViewModels.Search;

public sealed partial class SearchOverlayViewModel : ViewModelBase
{
    private readonly ISearchQueryService _search;
    private readonly FavoritesViewModel _favorites;
    private readonly HistoryViewModel _history;

    public SearchOverlayViewModel(
        IMessenger messenger,
        ISearchQueryService search,
        FavoritesViewModel favorites,
        HistoryViewModel history)
        : base(messenger)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _favorites = favorites ?? throw new ArgumentNullException(nameof(favorites));
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string? queryText;

    public ObservableCollection<string> Suggestions { get; } = new();

    public ObservableCollection<SearchHitDto> Results { get; } = new();

    public FavoritesViewModel Favorites => _favorites;

    public HistoryViewModel History => _history;

    [RelayCommand]
    private async Task OpenAsync()
    {
        IsOpen = true;
        StatusMessage = null;
        await Task.WhenAll(
            _favorites.LoadCommand.ExecuteAsync(null),
            _history.LoadCommand.ExecuteAsync(null));
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void CloseOnEsc(KeyRoutedEventArgs args)
    {
        if (args is not null && args.Key == Windows.System.VirtualKey.Escape)
        {
            IsOpen = false;
            args.Handled = true;
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

        await ExecuteSearchAsync(QueryText!).ConfigureAwait(false);
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

        var favoriteMatches = _favorites.Items
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name) && name.Contains(lower, StringComparison.OrdinalIgnoreCase))
            .Select(name => name);

        var historyMatches = _history.Items
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
    private async Task UseFavoriteAsync(SearchFavoriteItemModel? favorite)
    {
        if (favorite is null)
        {
            return;
        }

        QueryText = string.IsNullOrWhiteSpace(favorite.QueryText)
            ? favorite.MatchQuery
            : favorite.QueryText;

        if (!string.IsNullOrWhiteSpace(QueryText))
        {
            await ExecuteSearchAsync(QueryText!).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task UseHistoryAsync(SearchHistoryItemModel? historyItem)
    {
        if (historyItem is null)
        {
            return;
        }

        QueryText = historyItem.QueryText ?? historyItem.MatchQuery;

        if (!string.IsNullOrWhiteSpace(QueryText))
        {
            await ExecuteSearchAsync(QueryText!).ConfigureAwait(false);
        }
    }

    private async Task ExecuteSearchAsync(string query)
    {
        await SafeExecuteAsync(ct => SearchAsync(query, ct), "Vyhledávám…").ConfigureAwait(false);
    }

    private async Task SearchAsync(string query, CancellationToken ct)
    {
        Results.Clear();

        var hits = await _search.SearchAsync(query, 50, ct).ConfigureAwait(false);
        foreach (var hit in hits)
        {
            Results.Add(new SearchHitDto(hit.FileId, hit.Title, hit.Mime, hit.Snippet, hit.Score, hit.LastModifiedUtc));
        }

        StatusMessage = Results.Count == 0
            ? "Nebyl nalezen žádný výsledek."
            : $"Nalezeno {Results.Count} výsledků.";
    }
}
