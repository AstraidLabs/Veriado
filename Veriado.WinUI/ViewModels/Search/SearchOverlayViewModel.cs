using System;
using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Veriado.Application.Search.Abstractions;
using Veriado.Contracts.Search;

namespace Veriado.WinUI.ViewModels;

public partial class SearchOverlayViewModel : ObservableObject
{
    private readonly ISearchQueryService _search;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string? queryText;

    public ObservableCollection<string> Suggestions { get; } = new();

    public ObservableCollection<SearchHitDto> Results { get; } = new();

    public SearchOverlayViewModel(ISearchQueryService search)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
    }

    [RelayCommand]
    public void Open() => IsOpen = true;

    [RelayCommand]
    public void Close() => IsOpen = false;

    [RelayCommand]
    public void CloseOnEsc() => IsOpen = false;

    public async void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(QueryText))
        {
            return;
        }

        Results.Clear();
        var hits = await _search.SearchAsync(QueryText!, 50, CancellationToken.None).ConfigureAwait(true);
        foreach (var hit in hits)
        {
            Results.Add(new SearchHitDto(hit.FileId, hit.Title, hit.Mime, hit.Snippet, hit.Score, hit.LastModifiedUtc));
        }
    }

    public void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        QueryText = args.SelectedItem?.ToString();
    }
}
