using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Appl.Search.Abstractions;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class FilesSearchSuggestionsProvider : IFilesSearchSuggestionsProvider
{
    private const int SuggestionLimit = 50;
    private readonly ISearchFavoritesService _favoritesService;
    private readonly ISearchHistoryService _historyService;

    public FilesSearchSuggestionsProvider(
        ISearchFavoritesService favoritesService,
        ISearchHistoryService historyService)
    {
        _favoritesService = favoritesService ?? throw new ArgumentNullException(nameof(favoritesService));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
    }

    public async Task<IReadOnlyList<string>> GetSuggestionsAsync(CancellationToken cancellationToken)
    {
        var favoritesTask = _favoritesService.GetAllAsync(cancellationToken);
        var historyTask = _historyService.GetRecentAsync(SuggestionLimit, cancellationToken);

        await Task.WhenAll(favoritesTask, historyTask).ConfigureAwait(false);

        var favorites = favoritesTask.Result
            .Select(item => item.QueryText ?? item.MatchQuery)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!.Trim());

        var history = historyTask.Result
            .Select(item => item.QueryText ?? item.MatchQuery)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!.Trim());

        var suggestions = favorites
            .Concat(history)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(SuggestionLimit)
            .ToArray();

        return suggestions;
    }
}
