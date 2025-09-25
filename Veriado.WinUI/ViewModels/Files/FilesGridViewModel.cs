using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Files;
using Veriado.Contracts.Search;
using Veriado.WinUI.Services.Abstractions;
using Veriado.Services.Files;
using Veriado.WinUI.ViewModels.Base;
using Veriado.WinUI.Views;
using Veriado.Appl.Search.Abstractions;

namespace Veriado.WinUI.ViewModels.Files;

public sealed partial class FilesGridViewModel : ViewModelBase
{
    private readonly IFileQueryService _queryService;
    private readonly INavigationService _navigationService;
    private readonly Func<FileDetailView> _detailViewFactory;
    private readonly IHotStateService _hotState;
    private readonly ISearchHistoryService _historyService;

    [ObservableProperty]
    private partial string? searchText;

    [ObservableProperty]
    private partial int searchModeIndex;

    [ObservableProperty]
    private partial DateTimeOffset? createdFrom;

    [ObservableProperty]
    private partial DateTimeOffset? createdTo;

    [ObservableProperty]
    private partial double? lowerValue;

    [ObservableProperty]
    private partial double? upperValue;

    [ObservableProperty]
    private partial int pageSize = AppSettings.DefaultPageSize;

    public ObservableCollection<FileSummaryDto> Items { get; } = new();

    public ObservableCollection<string> SearchSuggestions { get; } = new();

    public ObservableCollection<string> QueryTokens { get; } = new();

    public ObservableCollection<SearchFavoriteItem> Favorites { get; } = new();

    public ObservableCollection<SearchHistoryEntry> History { get; } = new();

    public FilesGridViewModel(
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler,
        IFileQueryService queryService,
        INavigationService navigationService,
        IHotStateService hotState,
        Func<FileDetailView> detailViewFactory,
        ISearchHistoryService historyService)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _detailViewFactory = detailViewFactory ?? throw new ArgumentNullException(nameof(detailViewFactory));
        _hotState = hotState ?? throw new ArgumentNullException(nameof(hotState));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));

        PageSize = Math.Max(1, _hotState.PageSize);
        SearchText = _hotState.LastQuery;

        Messenger.RegisterAll(this);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await SafeExecuteAsync(async ct =>
        {
            var request = new FileGridQueryDto
            {
                Text = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                Page = 1,
                PageSize = Math.Max(1, PageSize),
            };

            var page = await _queryService.GetGridAsync(request, ct);

            Items.Clear();
            foreach (var item in page.Items)
            {
                Items.Add(item);
            }

            if (Items.Count == 0)
            {
                StatusService.Info("Žádné dokumenty neodpovídají aktuálnímu filtru.");
            }
            else
            {
                StatusService.Info($"Načteno {Items.Count} dokumentů.");
            }
        }, "Načítám dokumenty…");
    }

    [RelayCommand]
    private void OpenDetail(Guid id)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        var view = _detailViewFactory();
        _navigationService.NavigateDetail(view);

        if (view.DataContext is FileDetailViewModel detailViewModel)
        {
            _ = detailViewModel.LoadCommand.ExecuteAsync(id);
        }
    }

    partial void OnPageSizeChanged(int value)
    {
        var normalized = value <= 0 ? AppSettings.DefaultPageSize : value;
        if (normalized != value)
        {
            PageSize = normalized;
            return;
        }

        _hotState.PageSize = normalized;
    }

    partial void OnSearchTextChanged(string? value)
    {
        _hotState.LastQuery = string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
