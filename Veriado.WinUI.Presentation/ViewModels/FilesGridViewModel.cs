using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Collections;
using Microsoft.UI.Xaml.Controls;
using AutoMapper;
using Veriado.Presentation.Collections;
using Veriado.Presentation.Helpers;
using Veriado.Presentation.Messages;
using Veriado.Presentation.Models.Common;
using Veriado.Presentation.Models.Files;
using Veriado.Presentation.Models.Search;
using Veriado.Presentation.ViewModels.Files;
using Veriado.Services.Files;

namespace Veriado.Presentation.ViewModels;

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
public sealed partial class FilesGridViewModel : ViewModelBase,
    IRecipient<SearchRequestedMessage>,
    IRecipient<ImportCompletedMessage>
{
    private readonly IFileQueryService _fileQueryService;
    private readonly IMapper _mapper;
    private readonly AsyncDebouncer _refreshDebouncer = new(TimeSpan.FromMilliseconds(350));
    private bool _isLoadingMore;

    public FilesGridViewModel(
        IFileQueryService fileQueryService,
        IMapper mapper,
        SearchBarViewModel searchBar,
        FileFiltersViewModel filters,
        SortStateViewModel sortState,
        FavoritesViewModel favorites,
        HistoryViewModel history,
        IMessenger messenger)
        : base(messenger)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        SearchBar = searchBar ?? throw new ArgumentNullException(nameof(searchBar));
        Filters = filters ?? throw new ArgumentNullException(nameof(filters));
        SortState = sortState ?? throw new ArgumentNullException(nameof(sortState));
        Favorites = favorites ?? throw new ArgumentNullException(nameof(favorites));
        History = history ?? throw new ArgumentNullException(nameof(history));

        PagedState = new PagedQueryState();
        Items = CreateCollection();

        SearchBar.PropertyChanged += OnSearchBarPropertyChanged;
        SearchBar.Tokens.CollectionChanged += (_, _) => QueueRefresh();
        Filters.PropertyChanged += OnFiltersChanged;
        SortState.PropertyChanged += OnSortChanged;
        PagedState.PropertyChanged += OnPagedStateChanged;

        Messenger.Register<FilesGridViewModel, SearchRequestedMessage>(this, static (recipient, message) => recipient.Receive(message));
        Messenger.Register<FilesGridViewModel, ImportCompletedMessage>(this, static (recipient, message) => recipient.Receive(message));
    }

    /// <summary>
    /// Gets the incremental loading collection bound to the grid.
    /// </summary>
    [ObservableProperty]
    private IncrementalLoadingCollection<FilesIncrementalSource, FileSummaryModel>? items;

    /// <summary>
    /// Gets or sets the current segmented search mode.
    /// </summary>
    [ObservableProperty]
    private FileSearchMode searchMode = FileSearchMode.FullText;

    /// <summary>
    /// Gets or sets the search mode index used by the segmented control.
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

    /// <summary>
    /// Gets or sets the currently selected file identifier.
    /// </summary>
    [ObservableProperty]
    private Guid? selectedFileId;

    /// <summary>
    /// Gets the search bar view model.
    /// </summary>
    public SearchBarViewModel SearchBar { get; }

    /// <summary>
    /// Gets the filters view model.
    /// </summary>
    public FileFiltersViewModel Filters { get; }

    /// <summary>
    /// Gets the sort state view model.
    /// </summary>
    public SortStateViewModel SortState { get; }

    /// <summary>
    /// Gets the favourites view model.
    /// </summary>
    public FavoritesViewModel Favorites { get; }

    /// <summary>
    /// Gets the history view model.
    /// </summary>
    public HistoryViewModel History { get; }

    /// <summary>
    /// Gets the paging state.
    /// </summary>
    public PagedQueryState PagedState { get; }

    /// <summary>
    /// Initializes the grid by loading persisted search metadata and the first page of results.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadSuggestionsAsync(cancellationToken).ConfigureAwait(true);
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Initializes the grid when the page is loaded.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private Task LoadAsync(CancellationToken cancellationToken)
        => InitializeAsync(cancellationToken);

    /// <summary>
    /// Refreshes the grid by reloading the incremental source.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private Task RefreshAsync(CancellationToken cancellationToken)
        => ReloadAsync(cancellationToken);

    /// <summary>
    /// Executes the search using the current parameters.
    /// </summary>
    [RelayCommand]
    private void Search()
        => QueueRefresh();

    /// <summary>
    /// Sets the selected sort option.
    /// </summary>
    [RelayCommand]
    private void SetSort(FileSortOption? option)
    {
        if (option is not null && !ReferenceEquals(SortState.SelectedOption, option))
        {
            SortState.SelectedOption = option;
        }
    }

    /// <summary>
    /// Toggles the sort direction.
    /// </summary>
    [RelayCommand]
    private void ToggleSortDirection()
    {
        SortState.IsDescending = !SortState.IsDescending;
    }

    /// <summary>
    /// Navigates to the detail of the specified file.
    /// </summary>
    [RelayCommand]
    private void OpenDetail(Guid fileId)
    {
        if (fileId == Guid.Empty)
        {
            return;
        }

        SelectedFileId = fileId;
        Messenger.Send(new OpenFileDetailMessage(fileId));
    }

    /// <summary>
    /// Attempts to load additional items when the repeater approaches the end of the list.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task LoadMoreAsync(ScrollViewer? scrollViewer, CancellationToken cancellationToken)
    {
        if (scrollViewer is null || Items is null)
        {
            return;
        }

        var threshold = scrollViewer.ExtentHeight - scrollViewer.ViewportHeight - 200;
        if (scrollViewer.VerticalOffset < threshold)
        {
            return;
        }

        await EnsureMoreItemsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures additional items are loaded from the incremental source when available.
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
            await Items.LoadMoreItemsAsync((uint)Math.Max(PagedState.PageSize, 1)).AsTask(cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public void Receive(SearchRequestedMessage message)
    {
        if (!string.Equals(SearchBar.Query, message.Query, StringComparison.Ordinal))
        {
            SearchBar.Query = message.Query;
        }

        QueueRefresh();
    }

    /// <inheritdoc />
    public void Receive(ImportCompletedMessage message)
    {
        StatusMessage = $"Import dokončen: {message.Succeeded}/{message.Total}.";
        IsInfoBarOpen = true;
        QueueRefresh();
    }

    private IncrementalLoadingCollection<FilesIncrementalSource, FileSummaryModel> CreateCollection()
    {
        var incrementalSource = new FilesIncrementalSource(_fileQueryService, _mapper, BuildQuery, OnPageLoaded);
        return new IncrementalLoadingCollection<FilesIncrementalSource, FileSummaryModel>(incrementalSource, Math.Max(PagedState.PageSize, 1));
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await SafeExecuteAsync(
            async token =>
            {
                _isLoadingMore = false;
                Items = CreateCollection();
                await Items!.LoadMoreItemsAsync((uint)Math.Max(PagedState.PageSize, 1)).AsTask(token).ConfigureAwait(false);

                StatusMessage = Items.Count > 0
                    ? $"Načteno {Items.Count} z {PagedState.TotalCount} položek."
                    : "Žádné soubory nebyly nalezeny.";
                IsInfoBarOpen = true;
            },
            "Načítám soubory...",
            successMessage: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadSuggestionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            SearchBar.Suggestions.Clear();
            Favorites.Items.Clear();
            History.Items.Clear();

            var favorites = await _fileQueryService.GetFavoritesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var favorite in favorites.OrderBy(f => f.Position))
            {
                var favoriteModel = _mapper.Map<SearchFavoriteItemModel>(favorite);
                Favorites.Items.Add(favoriteModel);
                if (!string.IsNullOrWhiteSpace(favoriteModel.QueryText) && !SearchBar.Suggestions.Contains(favoriteModel.QueryText))
                {
                    SearchBar.Suggestions.Add(favoriteModel.QueryText);
                }
            }

            var historyEntries = await _fileQueryService.GetSearchHistoryAsync(15, cancellationToken).ConfigureAwait(false);
            foreach (var entry in historyEntries.OrderByDescending(h => h.LastQueriedUtc))
            {
                var historyModel = _mapper.Map<SearchHistoryEntryModel>(entry);
                History.Items.Add(historyModel);
                if (!string.IsNullOrWhiteSpace(historyModel.QueryText) && !SearchBar.Suggestions.Contains(historyModel.QueryText))
                {
                    SearchBar.Suggestions.Add(historyModel.QueryText);
                }
            }
        }
        catch
        {
            // Suggestions are best-effort and should not break the page.
        }
    }

    private FileGridQueryModel BuildQuery()
    {
        var model = new FileGridQueryModel
        {
            Text = string.IsNullOrWhiteSpace(SearchBar.Query) ? null : SearchBar.Query.Trim(),
            Fuzzy = SearchMode == FileSearchMode.Fuzzy,
            TextPrefix = SearchMode != FileSearchMode.Fuzzy,
            TextAllTerms = true,
        };

        model.Page.Page = 1;
        model.Page.PageSize = Math.Max(PagedState.PageSize, 1);

        model.Sort.Clear();
        model.Sort.Add(new FileSortSpecModel
        {
            Field = SortState.SelectedOption.Field switch
            {
                FileSortField.Created => nameof(FileSummaryModel.CreatedUtc),
                FileSortField.Name => nameof(FileSummaryModel.Name),
                FileSortField.Size => nameof(FileSummaryModel.Size),
                _ => nameof(FileSummaryModel.LastModifiedUtc),
            },
            Descending = SortState.IsDescending,
        });

        if (Filters.SizeFrom > 0)
        {
            model.SizeMin = (long)Math.Round(Filters.SizeFrom);
        }

        if (Filters.SizeTo > 0)
        {
            model.SizeMax = (long)Math.Round(Filters.SizeTo);
        }

        if (Filters.CreatedFrom is not null)
        {
            model.CreatedFromUtc = Filters.CreatedFrom;
        }

        if (Filters.CreatedTo is not null)
        {
            model.CreatedToUtc = Filters.CreatedTo;
        }

        foreach (var token in SearchBar.Tokens)
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
                    model.Mime = mime;
                }
            }
            else if (token.StartsWith("author:", StringComparison.OrdinalIgnoreCase))
            {
                var author = token["author:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(author))
                {
                    model.Author = author;
                }
            }
            else if (token.StartsWith("validity:", StringComparison.OrdinalIgnoreCase))
            {
                var validity = token["validity:".Length..].Trim();
                if (string.Equals(validity, "active", StringComparison.OrdinalIgnoreCase))
                {
                    model.IsCurrentlyValid = true;
                }
                else if (string.Equals(validity, "expired", StringComparison.OrdinalIgnoreCase))
                {
                    model.IsCurrentlyValid = false;
                }
            }
        }

        return model;
    }

    private void OnPageLoaded(PageResultModel<FileSummaryModel> page)
    {
        PagedState.TotalCount = page.TotalCount;
        LastError = null;
        IsInfoBarOpen = true;
    }

    private void OnSearchBarPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SearchBarViewModel.Query), StringComparison.Ordinal))
        {
            QueueRefresh();
        }
    }

    private void OnFiltersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        QueueRefresh();
    }

    private void OnSortChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        QueueRefresh();
    }

    partial void OnSearchModeChanged(FileSearchMode value)
    {
        OnPropertyChanged(nameof(SearchModeIndex));
        QueueRefresh();
    }

    private void OnPagedStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(PagedQueryState.PageSize), StringComparison.Ordinal))
        {
            if (PagedState.PageSize <= 0)
            {
                PagedState.PageSize = 25;
                return;
            }

            QueueRefresh();
        }
    }

    private void QueueRefresh()
        => _refreshDebouncer.Enqueue(() => ReloadAsync(CancellationToken.None));
}
