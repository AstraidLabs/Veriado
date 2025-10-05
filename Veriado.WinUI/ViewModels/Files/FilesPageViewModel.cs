using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly object _healthMonitorGate = new();
    private CancellationTokenSource? _healthMonitorSource;

    private static readonly TimeSpan HealthPollingInterval = TimeSpan.FromSeconds(15);
    private CancellationTokenSource? _searchDebounceSource;
    private readonly AsyncRelayCommand _nextPageCommand;
    private readonly AsyncRelayCommand _previousPageCommand;
    private readonly IReadOnlyList<int> _pageSizeOptions = new[] { 25, 50, 100, 150, 200 };
    private bool _suppressTargetPageChange;

    public FilesPageViewModel(
        IFileQueryService fileQueryService,
        IHotStateService hotStateService,
        IHealthService healthService,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _hotStateService = hotStateService ?? throw new ArgumentNullException(nameof(hotStateService));
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));

        Items = new ObservableCollection<FileSummaryDto>();
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
    private bool isIndexingPending;

    [ObservableProperty]
    private string? indexingWarningMessage;

    public double TargetPageMaximum
    {
        get
        {
            if (TotalPages > 0)
            {
                return TotalPages;
            }

            return 1;
        }
    }

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
        _hotStateService.LastQuery = value;
        DebounceRefresh();
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

        _ = RefreshAsync(false, clamped);
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
                await RefreshAsync(true).ConfigureAwait(false);
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

    private Task RefreshAsync() => RefreshAsync(true);

    private Task RefreshAsync(bool resetPage) => RefreshAsync(resetPage, null);

    private async Task RefreshAsync(bool resetPage, int? explicitPage)
    {
        CancelPendingDebounce();

        var page = explicitPage ?? (resetPage ? 1 : CurrentPage <= 0 ? 1 : CurrentPage);
        if (page <= 0)
        {
            page = 1;
        }

        await SafeExecuteAsync(async cancellationToken =>
        {
            var query = BuildQuery(page);
            var result = await _fileQueryService.GetGridAsync(query, cancellationToken).ConfigureAwait(false);

            await Dispatcher.Enqueue(() =>
            {
                Items.Clear();
                foreach (var item in result.Items)
                {
                    Items.Add(item);
                }

                UpdatePaginationState(result.Page, result.TotalPages, result.TotalCount, result.Items.Count);
            }).ConfigureAwait(false);
        });
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

        UpdatePaginationState(0, 0, 0, 0);

        await RefreshAsync(true).ConfigureAwait(false);
    }

    private FileGridQueryDto BuildQuery(int page)
    {
        var extension = string.IsNullOrWhiteSpace(ExtensionFilter) ? null : ExtensionFilter.Trim();
        var mime = string.IsNullOrWhiteSpace(MimeFilter) ? null : MimeFilter.Trim();
        var author = string.IsNullOrWhiteSpace(AuthorFilter) ? null : AuthorFilter.Trim();
        var version = ParseVersion(VersionFilter);

        var query = new FileGridQueryDto
        {
            Text = SearchText,
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
            using var registration = cancellationToken.Register(static state =>
            {
                if (state is PeriodicTimer timer)
                {
                    timer.Dispose();
                }
            }, timer);

            while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

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
        await RefreshAsync(false, target).ConfigureAwait(false);
    }

    private async Task LoadPreviousPageAsync()
    {
        var target = CurrentPage - 1;
        await RefreshAsync(false, target).ConfigureAwait(false);
    }

    private bool CanLoadNextPage() => TotalPages > 0 && CurrentPage < TotalPages;

    private bool CanLoadPreviousPage() => TotalPages > 0 && CurrentPage > 1;

    private void UpdatePaginationState(int currentPage, int totalPages, int totalCount, int itemsOnPage)
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
