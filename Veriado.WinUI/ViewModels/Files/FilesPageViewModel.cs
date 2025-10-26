using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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
using Veriado.WinUI.Views.Files;

namespace Veriado.WinUI.ViewModels.Files;

public partial class FilesPageViewModel : ViewModelBase
{
    private const int DefaultPageSize = 50;
    private const int DebounceDelayMilliseconds = 300;

    private readonly IFileQueryService _fileQueryService;
    private readonly IHotStateService _hotStateService;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly IHealthService _healthService;
    private readonly object _healthMonitorGate = new();
    private CancellationTokenSource? _healthMonitorSource;

    private readonly IDialogService _dialogService;
    private readonly IExceptionHandler _exceptionHandler;
    
    private static readonly TimeSpan HealthPollingInterval = TimeSpan.FromSeconds(15);
    private CancellationTokenSource? _searchDebounceSource;
    private readonly AsyncRelayCommand _nextPageCommand;
    private readonly AsyncRelayCommand _previousPageCommand;
    private readonly AsyncRelayCommand<FileSummaryDto?> _openDetailCommand;
    private readonly AsyncRelayCommand<FileSummaryDto?> _selectFileCommand;
    private readonly IReadOnlyList<int> _pageSizeOptions = new[] { 25, 50, 100, 150, 200 };
    private bool _suppressTargetPageChange;
    private readonly object _detailLoadGate = new();
    private CancellationTokenSource? _detailLoadSource;

    public FilesPageViewModel(
        IFileQueryService fileQueryService,
        IHotStateService hotStateService,
        IHealthService healthService,
        IFileOperationsService fileOperationsService,
        IDialogService dialogService,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _hotStateService = hotStateService ?? throw new ArgumentNullException(nameof(hotStateService));
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        _fileOperationsService = fileOperationsService ?? throw new ArgumentNullException(nameof(fileOperationsService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _exceptionHandler = exceptionHandler ?? throw new ArgumentNullException(nameof(exceptionHandler));

        Items = new ObservableCollection<FileSummaryDto>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ClearFiltersCommand = new AsyncRelayCommand(ClearFiltersAsync);

        _nextPageCommand = new AsyncRelayCommand(LoadNextPageAsync, CanLoadNextPage);
        _previousPageCommand = new AsyncRelayCommand(LoadPreviousPageAsync, CanLoadPreviousPage);
        _openDetailCommand = new AsyncRelayCommand<FileSummaryDto?>(ExecuteOpenDetailAsync);
        _selectFileCommand = new AsyncRelayCommand<FileSummaryDto?>(ExecuteSelectFileAsync);

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

    public IAsyncRelayCommand<FileSummaryDto?> OpenDetailCommand => _openDetailCommand;

    public IAsyncRelayCommand<FileSummaryDto?> SelectFileCommand => _selectFileCommand;

    public IReadOnlyList<int> PageSizeOptions => _pageSizeOptions;

    [ObservableProperty]
    private string? searchText;

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

    [ObservableProperty]
    private FileSummaryDto? selectedFile;

    [ObservableProperty]
    private FileDetailDto? selectedFileDetail;

    [ObservableProperty]
    private bool isDetailVisible;

    [ObservableProperty]
    private bool isDetailLoading;

    [ObservableProperty]
    private string? detailErrorMessage;

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

        _ = DebounceRefreshAsync(cts);
    }

    private async Task DebounceRefreshAsync(CancellationTokenSource cts)
    {
        try
        {
            var delayTask = Task.Delay(DebounceDelayMilliseconds);
            var cancellationTask = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (cts.Token.Register(static state =>
                   ((TaskCompletionSource<object?>)state!).TrySetResult(null), cancellationTask))
            {
                var completed = await Task.WhenAny(delayTask, cancellationTask.Task).ConfigureAwait(false);
                if (completed != delayTask)
                {
                    return;
                }
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (!ReferenceEquals(_searchDebounceSource, cts))
            {
                return;
            }

            await RefreshAsync(true, null, cts).ConfigureAwait(false);
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
    }

    private void CancelPendingDebounce()
    {
        var source = Interlocked.Exchange(ref _searchDebounceSource, null);
        if (source is null)
        {
            return;
        }

        if (!source.IsCancellationRequested)
        {
            source.Cancel();
        }
    }

    private Task RefreshAsync() => RefreshAsync(true);

    private Task RefreshAsync(bool resetPage) => RefreshAsync(resetPage, null);

    private Task RefreshAsync(bool resetPage, int? explicitPage) => RefreshAsync(resetPage, explicitPage, null);

    private async Task RefreshAsync(bool resetPage, int? explicitPage, CancellationTokenSource? debounceSource)
    {
        if (debounceSource is null)
        {
            CancelPendingDebounce();
        }
        else
        {
            var current = Interlocked.CompareExchange(ref _searchDebounceSource, null, debounceSource);
            if (!ReferenceEquals(current, debounceSource))
            {
                return;
            }

            if (debounceSource.IsCancellationRequested)
            {
                return;
            }
        }

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

                UpdateSelection(result.Items);
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

            while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await UpdateIndexingStatusAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Intentionally ignored.
        }
    }

    private async Task UpdateIndexingStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var staleDocuments = 0;

            var indexResult = await _healthService.GetIndexStatisticsAsync(cancellationToken).ConfigureAwait(false);
            if (indexResult.TryGetValue(out var indexStatistics))
            {
                staleDocuments = Math.Max(indexStatistics.StaleDocuments, 0);
            }

            var hasPendingIndexing = staleDocuments > 0;
            var message = hasPendingIndexing
                ? BuildIndexingWarningMessage(staleDocuments)
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

    private static string BuildIndexingWarningMessage(int staleDocuments)
    {
        var details = new List<string>();

        if (staleDocuments > 0)
        {
            details.Add($"{staleDocuments} dokumentů k přeindexování");
        }

        var suffix = details.Count > 0
            ? $" ({string.Join(", ", details)})"
            : string.Empty;

        return $"Probíhá indexace. Nechte aplikaci spuštěnou, dokud se proces nedokončí{suffix}.";
    }

    private Task ExecuteOpenDetailAsync(FileSummaryDto? summary)
    {
        if (summary is null)
        {
            return Task.CompletedTask;
        }

        return Dispatcher.EnqueueAsync(async () =>
        {
            var detailViewModel = new FileDetailDialogViewModel(
                summary,
                _fileQueryService,
                _fileOperationsService,
                Messenger,
                StatusService,
                Dispatcher,
                _exceptionHandler);

            void OnChangesSaved(object? sender, EventArgs args)
            {
                _ = RefreshCommand.ExecuteAsync(null);
            }

            detailViewModel.ChangesSaved += OnChangesSaved;

            var view = new FileDetailDialog
            {
                DataContext = detailViewModel,
            };

            _ = detailViewModel.InitializeAsync();

            await _dialogService.ShowAsync("Detail souboru", view, "Zavřít").ConfigureAwait(false);

            detailViewModel.ChangesSaved -= OnChangesSaved;
        });
    }

    private async Task ExecuteSelectFileAsync(FileSummaryDto? summary)
    {
        CancellationTokenSource? previous;
        CancellationTokenSource? current = null;

        lock (_detailLoadGate)
        {
            previous = _detailLoadSource;
            _detailLoadSource = null;

            if (summary is not null)
            {
                current = new CancellationTokenSource();
                _detailLoadSource = current;
            }
        }

        previous?.Cancel();
        previous?.Dispose();

        await Dispatcher.Enqueue(() =>
        {
            if (summary is null)
            {
                ClearDetailState();
            }
            else
            {
                SelectedFile = summary;
                SelectedFileDetail = null;
                DetailErrorMessage = null;
                IsDetailVisible = true;
                IsDetailLoading = true;
            }
        }).ConfigureAwait(false);

        if (summary is null || current is null)
        {
            return;
        }

        try
        {
            var detail = await _fileQueryService.GetDetailAsync(summary.Id, current.Token).ConfigureAwait(false);

            await Dispatcher.Enqueue(() =>
            {
                if (ReferenceEquals(_detailLoadSource, current) && !current.IsCancellationRequested)
                {
                    if (detail is null)
                    {
                        SelectedFileDetail = null;
                        DetailErrorMessage = "Soubor se nepodařilo načíst.";
                    }
                    else
                    {
                        SelectedFileDetail = detail;
                        DetailErrorMessage = null;
                    }
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Intentionally ignored.
        }
        catch (Exception ex)
        {
            var message = _exceptionHandler.Handle(ex);
            await Dispatcher.Enqueue(() =>
            {
                if (ReferenceEquals(_detailLoadSource, current))
                {
                    DetailErrorMessage = string.IsNullOrWhiteSpace(message)
                        ? "Nastala neočekávaná chyba při načítání detailu."
                        : message;
                }
            }).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(message))
            {
                StatusService.Error(message);
            }
        }
        finally
        {
            await Dispatcher.Enqueue(() =>
            {
                if (ReferenceEquals(_detailLoadSource, current))
                {
                    IsDetailLoading = false;
                }
            }).ConfigureAwait(false);

            lock (_detailLoadGate)
            {
                if (ReferenceEquals(_detailLoadSource, current))
                {
                    _detailLoadSource = null;
                }
            }

            current.Dispose();
        }
    }

    private void UpdateSelection(IReadOnlyCollection<FileSummaryDto> items)
    {
        if (SelectedFile is null)
        {
            return;
        }

        var updated = items.FirstOrDefault(item => item.Id == SelectedFile.Id);
        if (updated is null)
        {
            ClearDetailState();
            return;
        }

        if (!ReferenceEquals(updated, SelectedFile))
        {
            SelectedFile = updated;
        }
    }

    private void ClearDetailState()
    {
        SelectedFile = null;
        SelectedFileDetail = null;
        DetailErrorMessage = null;
        IsDetailVisible = false;
        IsDetailLoading = false;

        CancellationTokenSource? source;
        lock (_detailLoadGate)
        {
            source = _detailLoadSource;
            _detailLoadSource = null;
        }

        source?.Cancel();
        source?.Dispose();
    }
}
