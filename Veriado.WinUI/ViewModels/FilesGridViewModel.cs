using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Collections;
using Veriado.Application.Search.Abstractions;
using Veriado.Application.UseCases.Queries.FileGrid;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Files;
using Veriado.WinUI.Helpers;

namespace Veriado.WinUI.ViewModels;

/// <summary>
/// Defines the available search modes exposed through the segmented control.
/// </summary>
public enum FileSearchMode
{
    FullText = 0,
    Fuzzy = 1,
}

/// <summary>
/// View model powering the file grid with incremental loading, paging and advanced filtering.
/// </summary>
public sealed partial class FilesGridViewModel : ObservableObject
{
    private readonly IFileQueryService _fileQueryService;
    private readonly AsyncDebouncer _refreshDebouncer = new(TimeSpan.FromMilliseconds(350));

    public FilesGridViewModel(IFileQueryService fileQueryService)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));

        QueryTokens = new ObservableCollection<string>();
        SearchSuggestions = new ObservableCollection<string>();
        Favorites = new ObservableCollection<SearchFavoriteItem>();
        History = new ObservableCollection<SearchHistoryEntry>();

        PageSize = 50;
        GridItems = CreateCollection();
        FilteredItems = CreateView(GridItems);
    }

    /// <summary>
    /// Gets the incremental source used by the grid control.
    /// </summary>
    [ObservableProperty]
    private IncrementalLoadingCollection<FileSummaryIncrementalSource, FileSummaryDto> gridItems;

    /// <summary>
    /// Gets the advanced view used for local sorting and filtering.
    /// </summary>
    [ObservableProperty]
    private AdvancedCollectionView filteredItems;

    /// <summary>
    /// Gets the tokens applied through the tokenizing text box.
    /// </summary>
    public ObservableCollection<string> QueryTokens { get; }

    /// <summary>
    /// Gets the suggestion strings consumed by the rich suggest box.
    /// </summary>
    public ObservableCollection<string> SearchSuggestions { get; }

    /// <summary>
    /// Gets the stored favourite definitions fetched from the service layer.
    /// </summary>
    public ObservableCollection<SearchFavoriteItem> Favorites { get; }

    /// <summary>
    /// Gets the recent history entries used for the suggestion flyout.
    /// </summary>
    public ObservableCollection<SearchHistoryEntry> History { get; }

    [ObservableProperty]
    private FileSummaryDto? selectedItem;

    [ObservableProperty]
    private Guid? selectedFileId;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? searchText;

    [ObservableProperty]
    private FileSearchMode searchMode = FileSearchMode.FullText;

    [ObservableProperty]
    private double sizeMinimum;

    [ObservableProperty]
    private double sizeMaximum = 1024 * 1024 * 100;

    [ObservableProperty]
    private DateTimeOffset? createdFrom;

    [ObservableProperty]
    private DateTimeOffset? createdTo;

    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private int pageSize;

    [ObservableProperty]
    private bool showInfoBar;

    /// <summary>
    /// Initializes the grid by loading persisted search metadata and the first page of results.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadSuggestionsAsync(cancellationToken);
        await ReloadAsync(cancellationToken);
    }

    /// <summary>
    /// Applies the provided suggestion or favourite to the current search text.
    /// </summary>
    /// <param name="suggestion">The suggestion text.</param>
    [RelayCommand]
    private void ApplySuggestion(string suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            return;
        }

        SearchText = suggestion.Trim();
    }

    /// <summary>
    /// Adds a token from the tokenizing text box interaction.
    /// </summary>
    /// <param name="token">The token to add.</param>
    [RelayCommand]
    private void AddToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        if (QueryTokens.Any(existing => string.Equals(existing, token, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        QueryTokens.Add(token);
        FilteredItems?.RefreshFilter();
        QueueRefresh();
    }

    /// <summary>
    /// Removes the provided token.
    /// </summary>
    /// <param name="token">The token to remove.</param>
    [RelayCommand]
    private void RemoveToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var index = -1;
        for (var i = 0; i < QueryTokens.Count; i++)
        {
            if (string.Equals(QueryTokens[i], token, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        if (index >= 0)
        {
            QueryTokens.RemoveAt(index);
            FilteredItems?.RefreshFilter();
            QueueRefresh();
        }
    }

    /// <summary>
    /// Clears all filters and reloads the grid.
    /// </summary>
    [RelayCommand]
    private void ResetFilters()
    {
        SearchText = string.Empty;
        QueryTokens.Clear();
        SizeMinimum = 0;
        SizeMaximum = 1024 * 1024 * 100;
        CreatedFrom = null;
        CreatedTo = null;
        FilteredItems?.RefreshFilter();
        QueueRefresh();
    }

    /// <summary>
    /// Refreshes the grid by reloading the incremental source.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    [RelayCommand(IncludeCancelCommand = true)]
    private Task RefreshAsync(CancellationToken cancellationToken)
        => ReloadAsync(cancellationToken);

    /// <summary>
    /// Records the selection from the grid view and exposes the file identifier for navigation.
    /// </summary>
    /// <param name="summary">The clicked item.</param>
    [RelayCommand]
    private void SelectFile(FileSummaryDto? summary)
    {
        SelectedItem = summary;
        SelectedFileId = summary?.Id;
    }

    private AdvancedCollectionView CreateView(IncrementalLoadingCollection<FileSummaryIncrementalSource, FileSummaryDto> source)
    {
        var view = new AdvancedCollectionView(source, true)
        {
            IsLiveFiltering = true,
            IsLiveSorting = true,
            Filter = FilterByClientCriteria,
        };

        view.SortDescriptions.Add(new SortDescription(nameof(FileSummaryDto.LastModifiedUtc), SortDirection.Descending));
        return view;
    }

    private IncrementalLoadingCollection<FileSummaryIncrementalSource, FileSummaryDto> CreateCollection()
    {
        var incrementalSource = new FileSummaryIncrementalSource(_fileQueryService, BuildQuery, OnPageLoaded);
        return new IncrementalLoadingCollection<FileSummaryIncrementalSource, FileSummaryDto>(incrementalSource, PageSize);
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ShowInfoBar = true;
        StatusMessage = "Načítám soubory...";

        try
        {
            GridItems = CreateCollection();
            FilteredItems = CreateView(GridItems);

            // Trigger initial load.
            await GridItems.LoadMoreItemsAsync((uint)Math.Max(PageSize, 1)).AsTask(cancellationToken);

            StatusMessage = $"Načteno {GridItems.Count} z {TotalCount} položek.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Načítání bylo zrušeno.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Načítání selhalo: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSuggestionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            SearchSuggestions.Clear();
            var favorites = await _fileQueryService.GetFavoritesAsync(cancellationToken);
            Favorites.Clear();
            foreach (var favorite in favorites.OrderBy(f => f.Position))
            {
                Favorites.Add(favorite);
                if (!string.IsNullOrWhiteSpace(favorite.QueryText))
                {
                    SearchSuggestions.Add(favorite.QueryText);
                }
            }

            var history = await _fileQueryService.GetSearchHistoryAsync(20, cancellationToken);
            History.Clear();
            foreach (var entry in history.OrderByDescending(h => h.LastQueriedUtc))
            {
                History.Add(entry);
                if (!string.IsNullOrWhiteSpace(entry.QueryText) && !SearchSuggestions.Contains(entry.QueryText))
                {
                    SearchSuggestions.Add(entry.QueryText);
                }
            }
        }
        catch
        {
            // Suggestions are best-effort; ignore failures.
        }
    }

    private void QueueRefresh()
        => _refreshDebouncer.Enqueue(() => ReloadAsync(CancellationToken.None));

    /// <summary>
    /// Gets or sets the search mode as an index for segmented control bindings.
    /// </summary>
    public int SearchModeIndex
    {
        get => (int)SearchMode;
        set
        {
            var mode = Enum.IsDefined(typeof(FileSearchMode), value)
                ? (FileSearchMode)value
                : FileSearchMode.FullText;

            if (SearchMode != mode)
            {
                SearchMode = mode;
            }
        }
    }

    private bool FilterByClientCriteria(object? item)
    {
        if (item is not FileSummaryDto summary)
        {
            return false;
        }

        if (SizeMinimum > 0 && summary.Size < SizeMinimum)
        {
            return false;
        }

        if (SizeMaximum > 0 && summary.Size > SizeMaximum)
        {
            return false;
        }

        if (CreatedFrom is { } from && summary.CreatedUtc < from)
        {
            return false;
        }

        if (CreatedTo is { } to && summary.CreatedUtc > to)
        {
            return false;
        }

        if (QueryTokens.Count == 0)
        {
            return true;
        }

        foreach (var token in QueryTokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (token.StartsWith("mime:", StringComparison.OrdinalIgnoreCase))
            {
                var value = token["mime:".Length..].Trim();
                if (!string.IsNullOrEmpty(value) && !string.Equals(summary.Mime, value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else if (token.StartsWith("author:", StringComparison.OrdinalIgnoreCase))
            {
                var value = token["author:".Length..].Trim();
                if (!string.IsNullOrEmpty(value) && summary.Author?.IndexOf(value, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }
            else if (token.StartsWith("validity:", StringComparison.OrdinalIgnoreCase))
            {
                var value = token["validity:".Length..].Trim();
                var validity = summary.Validity;
                if (string.Equals(value, "active", StringComparison.OrdinalIgnoreCase))
                {
                    if (validity is null)
                    {
                        return false;
                    }

                    var now = DateTimeOffset.UtcNow;
                    if (validity.ValidUntil is { } until && until < now)
                    {
                        return false;
                    }
                }
                else if (string.Equals(value, "expired", StringComparison.OrdinalIgnoreCase))
                {
                    if (validity is null)
                    {
                        return false;
                    }

                    if (validity.ValidUntil is { } until && until >= DateTimeOffset.UtcNow)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private FileGridQueryDto BuildQuery()
    {
        var dto = new FileGridQueryDto
        {
            Page = new PageRequest
            {
                Page = 1,
                PageSize = Math.Max(PageSize, 1),
            },
        };

        dto.Sort.Clear();
        dto.Sort.Add(new FileSortSpecDto
        {
            Field = nameof(FileSummaryDto.LastModifiedUtc),
            Descending = true,
        });

        var mimeToken = QueryTokens.FirstOrDefault(t => t.StartsWith("mime:", StringComparison.OrdinalIgnoreCase));
        var authorToken = QueryTokens.FirstOrDefault(t => t.StartsWith("author:", StringComparison.OrdinalIgnoreCase));
        var validityToken = QueryTokens.FirstOrDefault(t => t.StartsWith("validity:", StringComparison.OrdinalIgnoreCase));

        dto = dto with
        {
            Text = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            Fuzzy = SearchMode == FileSearchMode.Fuzzy,
            TextPrefix = SearchMode != FileSearchMode.Fuzzy,
            TextAllTerms = true,
            SizeMin = SizeMinimum > 0 ? (long?)Math.Round(SizeMinimum) : null,
            SizeMax = SizeMaximum > 0 ? (long?)Math.Round(SizeMaximum) : null,
            CreatedFromUtc = CreatedFrom,
            CreatedToUtc = CreatedTo,
            Mime = mimeToken is null ? null : mimeToken["mime:".Length..].Trim(),
            Author = authorToken is null ? null : authorToken["author:".Length..].Trim(),
        };

        if (validityToken is not null)
        {
            var value = validityToken["validity:".Length..].Trim();
            if (string.Equals(value, "active", StringComparison.OrdinalIgnoreCase))
            {
                dto = dto with { IsCurrentlyValid = true };
            }
            else if (string.Equals(value, "expired", StringComparison.OrdinalIgnoreCase))
            {
                dto = dto with { IsCurrentlyValid = false };
            }
        }

        return dto;
    }

    private void OnPageLoaded(PageResult<FileSummaryDto> result)
    {
        TotalCount = result.TotalCount;
        ShowInfoBar = true;
        FilteredItems?.RefreshFilter();
    }

    partial void OnSearchTextChanged(string? value)
    {
        QueueRefresh();
    }

    partial void OnSearchModeChanged(FileSearchMode value)
    {
        OnPropertyChanged(nameof(SearchModeIndex));
        QueueRefresh();
    }

    partial void OnSizeMinimumChanged(double value)
    {
        FilteredItems?.RefreshFilter();
        QueueRefresh();
    }

    partial void OnSizeMaximumChanged(double value)
    {
        FilteredItems?.RefreshFilter();
        QueueRefresh();
    }

    partial void OnCreatedFromChanged(DateTimeOffset? value)
    {
        FilteredItems?.RefreshFilter();
        QueueRefresh();
    }

    partial void OnCreatedToChanged(DateTimeOffset? value)
    {
        FilteredItems?.RefreshFilter();
        QueueRefresh();
    }

    partial void OnPageSizeChanged(int value)
    {
        if (value <= 0)
        {
            PageSize = 25;
            return;
        }

        QueueRefresh();
    }

    private sealed class FileSummaryIncrementalSource : IIncrementalSource<FileSummaryDto>
    {
        private readonly IFileQueryService _fileQueryService;
        private readonly Func<FileGridQueryDto> _queryFactory;
        private readonly Action<PageResult<FileSummaryDto>> _pageCallback;

        public FileSummaryIncrementalSource(
            IFileQueryService fileQueryService,
            Func<FileGridQueryDto> queryFactory,
            Action<PageResult<FileSummaryDto>> pageCallback)
        {
            _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
            _queryFactory = queryFactory ?? throw new ArgumentNullException(nameof(queryFactory));
            _pageCallback = pageCallback ?? throw new ArgumentNullException(nameof(pageCallback));
        }

        public async Task<IEnumerable<FileSummaryDto>> GetPagedItemsAsync(int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            var dto = _queryFactory();
            var query = dto with
            {
                Page = dto.Page with
                {
                    Page = pageIndex + 1,
                    PageSize = pageSize,
                },
            };

            var result = await _fileQueryService.GetGridAsync(new FileGridQuery(query), cancellationToken);
            _pageCallback(result);
            return result.Items;
        }
    }
}
