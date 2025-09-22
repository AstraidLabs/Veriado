using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Collections;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Files;
using Veriado.WinUI.Collections;
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
    private bool _isLoadingMore;

    public FilesGridViewModel(IFileQueryService fileQueryService)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));

        QueryTokens = new ObservableCollection<string>();
        QueryTokens.CollectionChanged += OnQueryTokensChanged;

        SearchSuggestions = new ObservableCollection<string>();
        Favorites = new ObservableCollection<SearchFavoriteItem>();
        History = new ObservableCollection<SearchHistoryEntry>();

        PageSize = 50;
        SizeUpperBound = 1024 * 1024 * 100;
        Items = CreateCollection();
    }

    /// <summary>
    /// Gets the items displayed by the grid.
    /// </summary>
    [ObservableProperty]
    private IncrementalLoadingCollection<FilesIncrementalSource, FileSummaryDto> items;

    /// <summary>
    /// Gets or sets a value indicating whether the view model is loading data.
    /// </summary>
    [ObservableProperty]
    private bool isBusy;

    /// <summary>
    /// Gets or sets the search query text.
    /// </summary>
    [ObservableProperty]
    private string? queryText;

    /// <summary>
    /// Gets or sets the number of records requested per page.
    /// </summary>
    [ObservableProperty]
    private int pageSize;

    /// <summary>
    /// Gets or sets the informational status message displayed in the info bar.
    /// </summary>
    [ObservableProperty]
    private string? statusMessage;

    /// <summary>
    /// Gets or sets the error message displayed in the info bar.
    /// </summary>
    [ObservableProperty]
    private string? errorMessage;

    /// <summary>
    /// Gets or sets a value indicating whether the info bar should display an error state.
    /// </summary>
    [ObservableProperty]
    private bool hasError;

    /// <summary>
    /// Gets or sets a value indicating whether the status info bar is visible.
    /// </summary>
    [ObservableProperty]
    private bool isInfoBarOpen = true;

    /// <summary>
    /// Gets or sets the current segmented search mode.
    /// </summary>
    [ObservableProperty]
    private FileSearchMode searchMode = FileSearchMode.FullText;

    /// <summary>
    /// Gets or sets the minimum file size filter.
    /// </summary>
    [ObservableProperty]
    private double sizeLowerBound;

    /// <summary>
    /// Gets or sets the maximum file size filter.
    /// </summary>
    [ObservableProperty]
    private double sizeUpperBound;

    /// <summary>
    /// Gets or sets the minimum creation date filter.
    /// </summary>
    [ObservableProperty]
    private DateTimeOffset? createdFrom;

    /// <summary>
    /// Gets or sets the maximum creation date filter.
    /// </summary>
    [ObservableProperty]
    private DateTimeOffset? createdTo;

    /// <summary>
    /// Gets or sets the total count reported by the server.
    /// </summary>
    [ObservableProperty]
    private int totalCount;

    /// <summary>
    /// Gets or sets the currently selected file identifier.
    /// </summary>
    [ObservableProperty]
    private Guid? selectedFileId;

    /// <summary>
    /// Gets the advanced filter tokens bound to the tokenizing text box.
    /// </summary>
    public ObservableCollection<string> QueryTokens { get; }

    /// <summary>
    /// Gets the suggestions exposed by the rich suggest box.
    /// </summary>
    public ObservableCollection<string> SearchSuggestions { get; }

    /// <summary>
    /// Gets the favourite entries retrieved from the services layer.
    /// </summary>
    public ObservableCollection<SearchFavoriteItem> Favorites { get; }

    /// <summary>
    /// Gets the recent search history entries.
    /// </summary>
    public ObservableCollection<SearchHistoryEntry> History { get; }

    /// <summary>
    /// Occurs when the detail page should be opened.
    /// </summary>
    public event EventHandler<Guid>? DetailRequested;

    /// <summary>
    /// Initializes the grid by loading persisted search metadata and the first page of results.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadSuggestionsAsync(cancellationToken).ConfigureAwait(true);
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Refreshes the grid by reloading the incremental source.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Triggers a new search with the current parameters.
    /// </summary>
    [RelayCommand]
    private void Search()
    {
        QueueRefresh();
    }

    /// <summary>
    /// Raises navigation to the detail page for the specified file.
    /// </summary>
    /// <param name="fileId">The identifier of the file to open.</param>
    [RelayCommand]
    private void OpenDetail(Guid fileId)
    {
        if (fileId == Guid.Empty)
        {
            return;
        }

        SelectedFileId = fileId;
        DetailRequested?.Invoke(this, fileId);
    }

    /// <summary>
    /// Attempts to load additional items when the repeater approaches the end of the list.
    /// </summary>
    public async Task EnsureMoreItemsAsync(CancellationToken cancellationToken)
    {
        if (Items is null || _isLoadingMore || !Items.HasMoreItems)
        {
            return;
        }

        try
        {
            _isLoadingMore = true;
            await Items.LoadMoreItemsAsync((uint)Math.Max(PageSize, 1)).AsTask(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation triggered by rapid scrolling.
        }
        finally
        {
            _isLoadingMore = false;
        }
    }

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

    private IncrementalLoadingCollection<FilesIncrementalSource, FileSummaryDto> CreateCollection()
    {
        var incrementalSource = new FilesIncrementalSource(_fileQueryService, BuildQuery, OnPageLoaded);
        return new IncrementalLoadingCollection<FilesIncrementalSource, FileSummaryDto>(incrementalSource, PageSize);
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "Načítám soubory...";
        IsInfoBarOpen = true;

        try
        {
            _isLoadingMore = false;
            Items = CreateCollection();

            await Items.LoadMoreItemsAsync((uint)Math.Max(PageSize, 1)).AsTask(cancellationToken).ConfigureAwait(true);

            StatusMessage = Items.Count > 0
                ? $"Načteno {Items.Count} z {TotalCount} položek."
                : "Žádné soubory nebyly nalezeny.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Načítání bylo zrušeno.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Načítání selhalo: {ex.Message}";
            StatusMessage = "Načítání selhalo.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void QueueRefresh()
        => _refreshDebouncer.Enqueue(() => ReloadAsync(CancellationToken.None));

    private async Task LoadSuggestionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            SearchSuggestions.Clear();
            Favorites.Clear();
            History.Clear();

            var favorites = await _fileQueryService.GetFavoritesAsync(cancellationToken).ConfigureAwait(true);
            foreach (var favorite in favorites.OrderBy(f => f.Position))
            {
                Favorites.Add(favorite);
                if (!string.IsNullOrWhiteSpace(favorite.QueryText) && !SearchSuggestions.Contains(favorite.QueryText))
                {
                    SearchSuggestions.Add(favorite.QueryText);
                }
            }

            var historyEntries = await _fileQueryService.GetSearchHistoryAsync(15, cancellationToken).ConfigureAwait(true);
            foreach (var entry in historyEntries.OrderByDescending(h => h.LastQueriedUtc))
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
            // Suggestions are best-effort and should not break the page.
        }
    }

    private FileGridQueryDto BuildQuery()
    {
        var dto = new FileGridQueryDto
        {
            Text = string.IsNullOrWhiteSpace(QueryText) ? null : QueryText.Trim(),
            Fuzzy = SearchMode == FileSearchMode.Fuzzy,
            TextPrefix = SearchMode != FileSearchMode.Fuzzy,
            TextAllTerms = true,
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

        if (SizeLowerBound > 0)
        {
            dto = dto with { SizeMin = (long)Math.Round(SizeLowerBound) };
        }

        if (SizeUpperBound > 0)
        {
            dto = dto with { SizeMax = (long)Math.Round(SizeUpperBound) };
        }

        if (CreatedFrom is not null)
        {
            dto = dto with { CreatedFromUtc = CreatedFrom };
        }

        if (CreatedTo is not null)
        {
            dto = dto with { CreatedToUtc = CreatedTo };
        }

        foreach (var token in QueryTokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (token.StartsWith("mime:", StringComparison.OrdinalIgnoreCase))
            {
                var mime = token["mime:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(mime))
                {
                    dto = dto with { Mime = mime };
                }
            }
            else if (token.StartsWith("author:", StringComparison.OrdinalIgnoreCase))
            {
                var author = token["author:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(author))
                {
                    dto = dto with { Author = author };
                }
            }
            else if (token.StartsWith("validity:", StringComparison.OrdinalIgnoreCase))
            {
                var validity = token["validity:".Length..].Trim();
                if (string.Equals(validity, "active", StringComparison.OrdinalIgnoreCase))
                {
                    dto = dto with { IsCurrentlyValid = true };
                }
                else if (string.Equals(validity, "expired", StringComparison.OrdinalIgnoreCase))
                {
                    dto = dto with { IsCurrentlyValid = false };
                }
            }
        }

        return dto;
    }

    private void OnPageLoaded(PageResult<FileSummaryDto> page)
    {
        TotalCount = page.TotalCount;
        ErrorMessage = null;
        IsInfoBarOpen = true;
    }

    partial void OnErrorMessageChanged(string? value)
    {
        HasError = !string.IsNullOrWhiteSpace(value);
        if (HasError)
        {
            IsInfoBarOpen = true;
        }
    }

    private void OnQueryTokensChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        QueueRefresh();
    }

    partial void OnQueryTextChanged(string? value)
    {
        QueueRefresh();
    }

    partial void OnSearchModeChanged(FileSearchMode value)
    {
        OnPropertyChanged(nameof(SearchModeIndex));
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

    partial void OnSizeLowerBoundChanged(double value)
    {
        QueueRefresh();
    }

    partial void OnSizeUpperBoundChanged(double value)
    {
        QueueRefresh();
    }

    partial void OnCreatedFromChanged(DateTimeOffset? value)
    {
        QueueRefresh();
    }

    partial void OnCreatedToChanged(DateTimeOffset? value)
    {
        QueueRefresh();
    }
}
