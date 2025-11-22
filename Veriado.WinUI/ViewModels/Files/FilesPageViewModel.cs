using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Appl.Files;
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
    private readonly IFileService _fileService;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly IFileContentService _fileContentService;
    private readonly IPickerService _pickerService;
    private readonly object _healthMonitorGate = new();
    private CancellationTokenSource? _healthMonitorSource;

    private readonly IDialogService _dialogService;
    private readonly IExceptionHandler _exceptionHandler;
    private readonly ITimeFormattingService _timeFormattingService;
    private readonly IServerClock _serverClock;
    
    private static readonly TimeSpan HealthPollingInterval = TimeSpan.FromSeconds(15);
    private CancellationTokenSource? _searchDebounceSource;
    private readonly AsyncRelayCommand _nextPageCommand;
    private readonly AsyncRelayCommand _previousPageCommand;
    private readonly AsyncRelayCommand<FileSummaryDto?> _openDetailCommand;
    private readonly AsyncRelayCommand<FileSummaryDto?> _selectFileCommand;
    private readonly AsyncRelayCommand<FileSummaryDto?> _deleteFileCommand;
    private readonly IReadOnlyList<int> _pageSizeOptions = new[] { 25, 50, 100, 150, 200 };
    private bool _suppressTargetPageChange;
    private readonly object _detailLoadGate = new();
    private CancellationTokenSource? _detailLoadSource;
    private ValidityThresholds _validityThresholds;

    public FilesPageViewModel(
        IFileQueryService fileQueryService,
        IHotStateService hotStateService,
        IHealthService healthService,
        IFileService fileService,
        IFileOperationsService fileOperationsService,
        IFileContentService fileContentService,
        IPickerService pickerService,
        IDialogService dialogService,
        ITimeFormattingService timeFormattingService,
        IServerClock serverClock,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _hotStateService = hotStateService ?? throw new ArgumentNullException(nameof(hotStateService));
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _fileOperationsService = fileOperationsService ?? throw new ArgumentNullException(nameof(fileOperationsService));
        _fileContentService = fileContentService ?? throw new ArgumentNullException(nameof(fileContentService));
        _pickerService = pickerService ?? throw new ArgumentNullException(nameof(pickerService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _exceptionHandler = exceptionHandler ?? throw new ArgumentNullException(nameof(exceptionHandler));
        _timeFormattingService = timeFormattingService ?? throw new ArgumentNullException(nameof(timeFormattingService));
        _serverClock = serverClock ?? throw new ArgumentNullException(nameof(serverClock));

        Items = new ObservableCollection<FileListItemModel>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);

        _nextPageCommand = new AsyncRelayCommand(LoadNextPageAsync, CanLoadNextPage);
        _previousPageCommand = new AsyncRelayCommand(LoadPreviousPageAsync, CanLoadPreviousPage);
        _openDetailCommand = new AsyncRelayCommand<FileSummaryDto?>(ExecuteOpenDetailAsync, CanOpenDetail);
        _selectFileCommand = new AsyncRelayCommand<FileSummaryDto?>(ExecuteSelectFileAsync);
        _deleteFileCommand = new AsyncRelayCommand<FileSummaryDto?>(ExecuteDeleteFileAsync, CanDeleteFile);
        UpdateFileContentCommand = new AsyncRelayCommand<FileListItemModel?>(UpdateFileContentAsync);
        OpenFileCommand = new AsyncRelayCommand<FileListItemModel?>(OpenFileAsync);
        ShowInFolderCommand = new AsyncRelayCommand<FileListItemModel?>(ShowInFolderAsync);

        _suppressTargetPageChange = true;
        TargetPage = 1;
        _suppressTargetPageChange = false;

        PageSize = Math.Clamp(_hotStateService.PageSize <= 0 ? DefaultPageSize : _hotStateService.PageSize, 1, 200);
        SearchText = _hotStateService.LastQuery;
        _validityThresholds = _hotStateService.ValidityThresholds;
        if (_hotStateService is INotifyPropertyChanged observable)
        {
            observable.PropertyChanged += OnHotStatePropertyChanged;
        }
    }

    public ValidityThresholds ValidityThresholds => _validityThresholds;

    public ObservableCollection<FileListItemModel> Items { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand NextPageCommand => _nextPageCommand;

    public IAsyncRelayCommand PreviousPageCommand => _previousPageCommand;

    public IAsyncRelayCommand<FileSummaryDto?> OpenDetailCommand => _openDetailCommand;

    public IAsyncRelayCommand<FileSummaryDto?> SelectFileCommand => _selectFileCommand;

    public IAsyncRelayCommand<FileSummaryDto?> DeleteFileCommand => _deleteFileCommand;

    public IAsyncRelayCommand<FileListItemModel?> UpdateFileContentCommand { get; }

    public IAsyncRelayCommand<FileListItemModel?> OpenFileCommand { get; }

    public IAsyncRelayCommand<FileListItemModel?> ShowInFolderCommand { get; }

    public IReadOnlyList<int> PageSizeOptions => _pageSizeOptions;

    [ObservableProperty]
    private string? searchText;

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

    public void RefreshValidityStates()
    {
        RefreshValidityStates(_serverClock.NowLocal);
    }

    public void RefreshValidityStates(DateTimeOffset referenceTime)
    {
        foreach (var item in Items)
        {
            item.RecomputeValidity(referenceTime, _validityThresholds);
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
                var referenceTime = _serverClock.NowLocal;
                Items.Clear();
                foreach (var item in result.Items)
                {
                    Items.Add(new FileListItemModel(item, referenceTime, _validityThresholds));
                }

                RefreshValidityStates(referenceTime);
                UpdateSelection(result.Items);
                UpdatePaginationState(result.Page, result.TotalPages, result.TotalCount, result.Items.Count);
            }).ConfigureAwait(false);
        });
    }

    private FileGridQueryDto BuildQuery(int page)
    {
        var query = new FileGridQueryDto
        {
            Text = SearchText,
            Page = page,
            PageSize = Math.Clamp(PageSize <= 0 ? DefaultPageSize : PageSize, 1, 200),
        };

        return query;
    }

    private void OnHotStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IHotStateService.ValidityRedThresholdDays)
            or nameof(IHotStateService.ValidityOrangeThresholdDays)
            or nameof(IHotStateService.ValidityGreenThresholdDays)
            or nameof(IHotStateService.ValidityThresholds))
        {
            var updated = _hotStateService.ValidityThresholds;
            if (updated != _validityThresholds)
            {
                _validityThresholds = updated;
                RefreshValidityStates(_serverClock.NowLocal);
            }
        }
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

    private async Task UpdateFileContentAsync(FileListItemModel? item)
    {
        if (item is null)
        {
            return;
        }

        string[]? selection;
        try
        {
            selection = await _pickerService
                .PickFilesAsync(
                    string.IsNullOrWhiteSpace(item.Dto.Extension) ? null : new[] { item.Dto.Extension },
                    multiple: false)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var message = _exceptionHandler.Handle(ex);
            if (!string.IsNullOrWhiteSpace(message))
            {
                StatusService.Error(message);
            }

            return;
        }

        var filePath = selection?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (!File.Exists(filePath))
        {
            var exception = new FileNotFoundException($"Zdrojový soubor '{filePath}' nebyl nalezen.", filePath);
            var message = _exceptionHandler.Handle(exception);
            if (!string.IsNullOrWhiteSpace(message))
            {
                StatusService.Error(message);
            }

            return;
        }

        await SafeExecuteAsync(
            async cancellationToken =>
            {
                var response = await _fileOperationsService
                    .ReplaceFileContentAsync(item.Dto.Id, filePath, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccess || response.Data is null)
                {
                    var message = response.Errors.FirstOrDefault()?.Message
                        ?? "Nepodařilo se aktualizovat obsah souboru.";
                    await Dispatcher.Enqueue(() => StatusService.Error(message)).ConfigureAwait(false);
                    return;
                }

                var updatedModel = new FileListItemModel(
                    response.Data,
                    _serverClock.NowLocal,
                    _validityThresholds);

                await Dispatcher.Enqueue(() =>
                {
                    var index = Items.IndexOf(item);
                    if (index >= 0)
                    {
                        Items[index] = updatedModel;
                    }

                    if (SelectedFile?.Id == response.Data.Id)
                    {
                        SelectedFile = response.Data;
                        SelectedFileDetail = null;
                    }
                }).ConfigureAwait(false);
            },
            "Aktualizace souboru...")
            .ConfigureAwait(false);
    }

    private async Task OpenFileAsync(FileListItemModel? item)
    {
        if (item is null)
        {
            return;
        }

        await SafeExecuteAsync(
            cancellationToken => _fileContentService.OpenInDefaultAppAsync(item.Dto.Id, cancellationToken),
            "Otevírání souboru...")
            .ConfigureAwait(false);
    }

    private async Task ShowInFolderAsync(FileListItemModel? item)
    {
        if (item is null)
        {
            return;
        }

        await SafeExecuteAsync(
            cancellationToken => _fileContentService.ShowInFolderAsync(item.Dto.Id, cancellationToken),
            "Otevírání umístění souboru...")
            .ConfigureAwait(false);
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

    private static bool CanOpenDetail(FileSummaryDto? summary) => summary is not null;

    private static bool CanDeleteFile(FileSummaryDto? summary) => summary is not null;

    private async Task ExecuteOpenDetailAsync(FileSummaryDto? summary)
    {
        if (summary is null)
        {
            return;
        }

        using var dialogCts = new CancellationTokenSource();

        try
        {
            await using var dialogScope = _dialogService.CreateViewModel<FileDetailDialogViewModel>();
            var dialogViewModel = dialogScope.ViewModel;
            await dialogViewModel.LoadAsync(summary.Id, dialogCts.Token).ConfigureAwait(false);

            var result = await _dialogService.ShowDialogAsync(dialogViewModel, dialogCts.Token).ConfigureAwait(false);

            if (result.IsPrimary)
            {
                await RefreshCommand.ExecuteAsync(null);
            }
        }
        catch (OperationCanceledException)
        {
            // Intentionally ignored.
        }
        catch (Exception ex)
        {
            var message = _exceptionHandler.Handle(ex);
            if (!string.IsNullOrWhiteSpace(message))
            {
                StatusService.Error(message);
            }
        }
    }

    private async Task ExecuteDeleteFileAsync(FileSummaryDto? summary)
    {
        if (summary is null)
        {
            return;
        }

        var confirmed = await ConfirmFileDeletionAsync(summary).ConfigureAwait(false);

        if (!confirmed)
        {
            return;
        }

        var deleted = false;
        await SafeExecuteAsync(
            async cancellationToken =>
            {
                await _fileService.DeleteAsync(summary.Id, cancellationToken).ConfigureAwait(false);
                deleted = true;
            },
            "Mazání souboru...")
            .ConfigureAwait(false);

        if (!deleted)
        {
            return;
        }

        await Dispatcher.Enqueue(() => ClearDetailState()).ConfigureAwait(false);
        await RefreshCommand.ExecuteAsync(null);
    }

    private Task<bool> ConfirmFileDeletionAsync(FileSummaryDto summary)
    {
        var fileDisplayName = string.IsNullOrWhiteSpace(summary.Extension)
            ? summary.Name
            : $"{summary.Name}.{summary.Extension}";

        return _dialogService.ConfirmAsync(
            "Smazat soubor",
            $"Opravdu chcete smazat soubor \"{fileDisplayName}\"?",
            "Smazat",
            "Zrušit");
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
