using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Files;
using Veriado.Services.Files;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Files;

public partial class FilesPageViewModel : ViewModelBase
{
    private const int DefaultPageSize = 50;
    private const int DebounceDelayMilliseconds = 300;

    private readonly IFileQueryService _fileQueryService;
    private readonly IHotStateService _hotStateService;
    private CancellationTokenSource? _searchDebounceSource;

    private int _currentPage;
    private int _totalPages;
    private int _totalCount;

    public FilesPageViewModel(
        IFileQueryService fileQueryService,
        IHotStateService hotStateService,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _hotStateService = hotStateService ?? throw new ArgumentNullException(nameof(hotStateService));

        Items = new ObservableCollection<FileSummaryDto>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ClearFiltersCommand = new AsyncRelayCommand(ClearFiltersAsync);

        PageSize = Math.Clamp(_hotStateService.PageSize <= 0 ? DefaultPageSize : _hotStateService.PageSize, 1, 200);
        SearchText = _hotStateService.LastQuery;
    }

    public ObservableCollection<FileSummaryDto> Items { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand ClearFiltersCommand { get; }

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

    partial void OnSearchTextChanged(string? value)
    {
        _hotStateService.LastQuery = value;
        DebounceRefresh();
    }

    partial void OnFuzzyChanged(bool value)
    {
        DebounceRefresh();
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

    private async Task RefreshAsync(bool resetPage)
    {
        CancelPendingDebounce();

        if (!resetPage && _totalPages != 0 && _currentPage >= _totalPages)
        {
            return;
        }

        var page = resetPage ? 1 : Math.Max(1, _currentPage + 1);

        await SafeExecuteAsync(async cancellationToken =>
        {
            var query = BuildQuery(page);
            var result = await _fileQueryService.GetGridAsync(query, cancellationToken).ConfigureAwait(false);

            await Dispatcher.Enqueue(() =>
            {
                if (resetPage)
                {
                    Items.Clear();
                }

                foreach (var item in result.Items)
                {
                    Items.Add(item);
                }

                _currentPage = result.TotalPages == 0 ? 0 : result.Page;
                _totalPages = result.TotalPages;
                _totalCount = result.TotalCount;

                var shown = Items.Count;
                var pageDisplay = _totalPages == 0 ? 0 : _currentPage;
                StatusText = $"{shown}/{_totalCount} · {pageDisplay}/{_totalPages}";
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

        _currentPage = 0;
        _totalPages = 0;
        _totalCount = 0;

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
}
