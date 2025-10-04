using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Files;
using Veriado.Services.Files;
using Veriado.Services.Diagnostics;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Files;

public partial class FilesPageViewModel : ViewModelBase
{
    private const int DefaultPageSize = 50;
    private const int DebounceDelayMilliseconds = 300;

    private readonly IFileQueryService _fileQueryService;
    private readonly IHotStateService _hotStateService;
    private readonly IHealthService _healthService;
    private readonly IFilesSearchSuggestionsProvider _searchSuggestionsProvider;
    private readonly object _healthMonitorGate = new();
    private CancellationTokenSource? _healthMonitorSource;

    private static readonly TimeSpan HealthPollingInterval = TimeSpan.FromSeconds(15);
    private CancellationTokenSource? _searchDebounceSource;
    private CancellationTokenSource? _activeSearchSource;
    private readonly AsyncRelayCommand _nextPageCommand;
    private readonly AsyncRelayCommand _previousPageCommand;
    private readonly IReadOnlyList<int> _pageSizeOptions = new[] { 25, 50, 100, 150, 200 };
    private bool _suppressTargetPageChange;

    public FilesPageViewModel(
        IFileQueryService fileQueryService,
        IHotStateService hotStateService,
        IHealthService healthService,
        IFilesSearchSuggestionsProvider searchSuggestionsProvider,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _hotStateService = hotStateService ?? throw new ArgumentNullException(nameof(hotStateService));
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        _searchSuggestionsProvider = searchSuggestionsProvider ?? throw new ArgumentNullException(nameof(searchSuggestionsProvider));

        Items = new ObservableCollection<FileSummaryDto>();
        SearchSuggestions = new ObservableCollection<string>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ClearFiltersCommand = new AsyncRelayCommand(ClearFiltersAsync);

        _nextPageCommand = new AsyncRelayCommand(LoadNextPageAsync, CanLoadNextPage);
        _previousPageCommand = new AsyncRelayCommand(LoadPreviousPageAsync, CanLoadPreviousPage);

        _suppressTargetPageChange = true;
        TargetPage = 1;
        _suppressTargetPageChange = false;

        PageSize = Math.Clamp(_hotStateService.PageSize <= 0 ? DefaultPageSize : _hotStateService.PageSize, 1, 200);
        SearchText = _hotStateService.LastQuery;
    }

    public ObservableCollection<FileSummaryDto> Items { get; }

    public ObservableCollection<string> SearchSuggestions { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand ClearFiltersCommand { get; }

    public IAsyncRelayCommand NextPageCommand => _nextPageCommand;

    public IAsyncRelayCommand PreviousPageCommand => _previousPageCommand;

    public IReadOnlyList<int> PageSizeOptions => _pageSizeOptions;

    [ObservableProperty]
    private string? searchText;

    [ObservableProperty]
    private bool fuzzy;

    [ObservableProperty]
    private string? extensionFilter;

    [ObservableProperty]
    private string? mimeFilter;

    [ObservableProperty]
    private string? authorFilter;

    [ObservableProperty]
    private string? versionFilter;

    [ObservableProperty]
    private bool? readOnlyFilter;

    [ObservableProperty]
    private bool? isIndexStaleFilter;

    [ObservableProperty]
    private bool? hasValidityFilter;

    [ObservableProperty]
    private bool? currentlyValidFilter;

    [ObservableProperty]
    private int? expiringInDaysFilter;

    [ObservableProperty]
    private long? sizeMinFilter;

    [ObservableProperty]
    private long? sizeMaxFilter;

    [ObservableProperty]
    private DateTimeOffset? createdFromFilter;

    [ObservableProperty]
    private DateTimeOffset? createdToFilter;

    [ObservableProperty]
    private DateTimeOffset? modifiedFromFilter;

    [ObservableProperty]
    private DateTimeOffset? modifiedToFilter;

    [ObservableProperty]
    private int pageSize = DefaultPageSize;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private int currentPage;

    [ObservableProperty]
    private int totalPages;

    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private double targetPage;

    [ObservableProperty]
    private bool hasMorePages;

    [ObservableProperty]
    private bool isTruncated;

    [ObservableProperty]
    private bool isIndexingPending;

    [ObservableProperty]
    private string? indexingWarningMessage;

    public double TargetPageMaximum => TotalPages > 0 ? TotalPages : 0;

    partial void OnTotalPagesChanged(int value)
    {
        OnPropertyChanged(nameof(TargetPageMaximum));
    }

    public void StartHealthMonitoring()
    {
        lock (_healthMonitorGate)
        {
            if (_healthMonitorSource is not null)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _healthMonitorSource = cts;
            _ = MonitorHealthStatusAsync(cts.Token);
        }
    }

    public async Task LoadSearchSuggestionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var suggestions = await _searchSuggestionsProvider.GetSuggestionsAsync(cancellationToken).ConfigureAwait(false);

            await Dispatcher.Enqueue(() =>
            {
                SearchSuggestions.Clear();
                foreach (var suggestion in suggestions)
                {
                    SearchSuggestions.Add(suggestion);
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await Dispatcher.Enqueue(() => SearchSuggestions.Clear()).ConfigureAwait(false);
        }
    }

    public void StopHealthMonitoring()
    {
        CancellationTokenSource? source;
        lock (_healthMonitorGate)
        {
            source = _healthMonitorSource;
            _healthMonitorSource = null;
        }

        if (source is null)
        {
            return;
        }

        source.Cancel();
        source.Dispose();

        _ = Dispatcher.Enqueue(() =>
        {
            IsIndexingPending = false;
            IndexingWarningMessage = null;
        });
    }

    partial void OnSearchTextChanged(string? value)
    {
        var sanitized = NormalizeSearchText(value);
        if (!string.Equals(value, sanitized, StringComparison.Ordinal))
        {
            SearchText = sanitized;
            return;
        }

        _hotStateService.LastQuery = sanitized;
        DebounceRefresh();
    }

    partial void OnExtensionFilterChanged(string? value)
    {
        var sanitized = NormalizeExtension(value);
        if (!string.Equals(value, sanitized, StringComparison.Ordinal))
        {
            ExtensionFilter = sanitized;
        }
    }

    partial void OnFuzzyChanged(bool value)
    {
        DebounceRefresh();
    }

    partial void OnTargetPageChanged(double value)
    {
        if (_suppressTargetPageChange)
        {
            return;
        }

        if (TotalPages <= 0)
        {
            return;
        }

        var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        var clamped = Math.Clamp(rounded, 1, TotalPages);
        if (Math.Abs(value - clamped) > double.Epsilon)
        {
            try
            {
                _suppressTargetPageChange = true;
                TargetPage = clamped;
            }
            finally
            {
                _suppressTargetPageChange = false;
            }

            return;
        }

        if (clamped == CurrentPage)
        {
            return;
        }

        _ = RefreshAsync(false, clamped, CancellationToken.None);
    }

    partial void OnPageSizeChanged(int value)
    {
        var clamped = Math.Clamp(value <= 0 ? DefaultPageSize : value, 1, 200);
        if (clamped != value)
        {
            PageSize = clamped;
            return;
        }

        _hotStateService.PageSize = clamped;
        DebounceRefresh();
    }

    private void DebounceRefresh()
    {
        CancelPendingDebounce();

        var cts = new CancellationTokenSource();
        _searchDebounceSource = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelayMilliseconds, cts.Token).ConfigureAwait(false);
                cts.Token.ThrowIfCancellationRequested();
                await RefreshAsync(true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Intentionally ignored.
            }
            finally
            {
                if (ReferenceEquals(_searchDebounceSource, cts))
                {
                    _searchDebounceSource = null;
                }

                cts.Dispose();
            }
        });
    }

    private void CancelPendingDebounce()
    {
        if (_searchDebounceSource is null)
        {
            return;
        }

        _searchDebounceSource.Cancel();
        _searchDebounceSource.Dispose();
        _searchDebounceSource = null;
    }

    private Task RefreshAsync(CancellationToken cancellationToken) => RefreshAsync(true, null, cancellationToken);

    private Task RefreshAsync(bool resetPage, CancellationToken cancellationToken) => RefreshAsync(resetPage, null, cancellationToken);

    private async Task RefreshAsync(bool resetPage, int? explicitPage, CancellationToken cancellationToken)
    {
        CancelPendingDebounce();

        var page = explicitPage ?? (resetPage ? 1 : CurrentPage <= 0 ? 1 : CurrentPage);
        if (page <= 0)
        {
            page = 1;
        }

        await ExecuteSearchAsync(page, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteSearchAsync(int page, CancellationToken commandToken)
    {
        commandToken.ThrowIfCancellationRequested();

        TryCancelRunning();

        if (_activeSearchSource is not null)
        {
            _activeSearchSource.Cancel();
            _activeSearchSource.Dispose();
            _activeSearchSource = null;
        }

        await WaitForSearchCompletionAsync(commandToken).ConfigureAwait(false);

        var searchScope = CancellationTokenSource.CreateLinkedTokenSource(commandToken);
        _activeSearchSource = searchScope;

        try
        {
            await SafeExecuteAsync(async innerToken =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(innerToken, searchScope.Token);
                var query = BuildQuery(page);
                var result = await _fileQueryService.GetGridAsync(query, linked.Token).ConfigureAwait(false);

                await Dispatcher.Enqueue(() =>
                {
                    Items.Clear();
                    foreach (var item in result.Items)
                    {
                        Items.Add(item);
                    }

                    UpdatePaginationState(result.Page, result.TotalPages, result.TotalCount, result.Items.Count, result.HasMore, result.IsTruncated);
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        finally
        {
            searchScope.Dispose();
            if (ReferenceEquals(_activeSearchSource, searchScope))
            {
                _activeSearchSource = null;
            }
        }
    }

    private async Task WaitForSearchCompletionAsync(CancellationToken cancellationToken)
    {
        while (IsBusy)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ClearFiltersAsync()
    {
        ExtensionFilter = null;
        MimeFilter = null;
        AuthorFilter = null;
        VersionFilter = null;
        ReadOnlyFilter = null;
        IsIndexStaleFilter = null;
        HasValidityFilter = null;
        CurrentlyValidFilter = null;
        ExpiringInDaysFilter = null;
        SizeMinFilter = null;
        SizeMaxFilter = null;
        CreatedFromFilter = null;
        CreatedToFilter = null;
        ModifiedFromFilter = null;
        ModifiedToFilter = null;

        UpdatePaginationState(0, 0, 0, 0, false, false);

        await RefreshAsync(true, CancellationToken.None).ConfigureAwait(false);
    }

    private FileGridQueryDto BuildQuery(int page)
    {
        var searchText = NormalizeSearchText(SearchText);
        var extension = NormalizeExtension(ExtensionFilter);
        var mime = string.IsNullOrWhiteSpace(MimeFilter) ? null : MimeFilter.Trim();
        var author = string.IsNullOrWhiteSpace(AuthorFilter) ? null : AuthorFilter.Trim();
        var version = ParseVersion(VersionFilter);

        var query = new FileGridQueryDto
        {
            Text = searchText,
            Fuzzy = Fuzzy,
            Extension = extension,
            Mime = mime,
            Author = author,
            Version = version,
            IsReadOnly = ReadOnlyFilter,
            IsIndexStale = IsIndexStaleFilter,
            HasValidity = HasValidityFilter,
            IsCurrentlyValid = CurrentlyValidFilter,
            ExpiringInDays = ExpiringInDaysFilter,
            SizeMin = SizeMinFilter,
            SizeMax = SizeMaxFilter,
            CreatedFromUtc = CreatedFromFilter,
            CreatedToUtc = CreatedToFilter,
            ModifiedFromUtc = ModifiedFromFilter,
            ModifiedToUtc = ModifiedToFilter,
            Page = page,
            PageSize = Math.Clamp(PageSize <= 0 ? DefaultPageSize : PageSize, 1, 200),
        };

        return query;
    }

    private static string? NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? NormalizeExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var normalized = trimmed.TrimStart('.');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.ToLowerInvariant();
    }

    private static int? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private async Task MonitorHealthStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await UpdateIndexingStatusAsync(cancellationToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(HealthPollingInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await UpdateIndexingStatusAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Intentionally ignored.
        }
    }

    private async Task UpdateIndexingStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pendingOutboxEvents = 0;
            var staleDocuments = 0;

            var healthResult = await _healthService.GetAsync(cancellationToken).ConfigureAwait(false);
            if (healthResult.TryGetValue(out var healthStatus))
            {
                pendingOutboxEvents = Math.Max(healthStatus.PendingOutboxEvents, 0);
            }

            var indexResult = await _healthService.GetIndexStatisticsAsync(cancellationToken).ConfigureAwait(false);
            if (indexResult.TryGetValue(out var indexStatistics))
            {
                staleDocuments = Math.Max(indexStatistics.StaleDocuments, 0);
            }

            var hasPendingIndexing = pendingOutboxEvents > 0 || staleDocuments > 0;
            var message = hasPendingIndexing
                ? BuildIndexingWarningMessage(pendingOutboxEvents, staleDocuments)
                : null;

            await Dispatcher.Enqueue(() =>
            {
                IsIndexingPending = hasPendingIndexing;
                IndexingWarningMessage = message;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await Dispatcher.Enqueue(() =>
            {
                IsIndexingPending = false;
                IndexingWarningMessage = null;
            }).ConfigureAwait(false);
        }
    }

    private async Task LoadNextPageAsync()
    {
        var target = CurrentPage + 1;
        await RefreshAsync(false, target, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task LoadPreviousPageAsync()
    {
        var target = CurrentPage - 1;
        await RefreshAsync(false, target, CancellationToken.None).ConfigureAwait(false);
    }

    private bool CanLoadNextPage() => HasMorePages;

    private bool CanLoadPreviousPage() => TotalPages > 0 && CurrentPage > 1;

    private void UpdatePaginationState(int currentPage, int totalPages, int totalCount, int itemsOnPage, bool hasMore, bool isTruncated)
    {
        if (totalPages <= 0)
        {
            currentPage = 0;
            totalPages = 0;
            totalCount = Math.Max(totalCount, 0);
        }
        else
        {
            currentPage = Math.Clamp(currentPage <= 0 ? 1 : currentPage, 1, totalPages);
        }

        CurrentPage = currentPage;
        TotalPages = totalPages;
        TotalCount = totalCount;
        HasMorePages = hasMore;
        IsTruncated = isTruncated;

        try
        {
            _suppressTargetPageChange = true;
            TargetPage = totalPages == 0 ? 0 : currentPage;
        }
        finally
        {
            _suppressTargetPageChange = false;
        }

        var pageDisplay = totalPages == 0 ? 0 : currentPage;
        StatusText = $"{itemsOnPage}/{totalCount} · {pageDisplay}/{totalPages}";

        _nextPageCommand.NotifyCanExecuteChanged();
        _previousPageCommand.NotifyCanExecuteChanged();
    }

    private static string BuildIndexingWarningMessage(int pendingOutboxEvents, int staleDocuments)
    {
        var details = new List<string>();

        if (pendingOutboxEvents > 0)
        {
            details.Add($"{pendingOutboxEvents} čekajících událostí fronty");
        }

        if (staleDocuments > 0)
        {
            details.Add($"{staleDocuments} dokumentů k přeindexování");
        }

        var suffix = details.Count > 0
            ? $" ({string.Join(", ", details)})"
            : string.Empty;

        return $"Probíhá indexace. Nechte aplikaci spuštěnou, dokud se proces nedokončí{suffix}.";
    }
}
