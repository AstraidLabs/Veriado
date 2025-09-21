// BEGIN CHANGE Veriado.WinUI/ViewModels/GridViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Application.Search.Abstractions;
using Veriado.Application.UseCases.Queries.FileGrid;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Files;
using Veriado.WinUI.Messages;

namespace Veriado.WinUI.ViewModels;

/// <summary>
/// Provides the presentation logic for the file grid including search and paging.
/// </summary>
public sealed partial class GridViewModel : BaseViewModel, IRecipient<GridRefreshMessage>
{
    private readonly IFileQueryService _fileQueryService;
    private readonly TimeSpan _searchDebounce = TimeSpan.FromMilliseconds(350);
    private CancellationTokenSource? _searchCts;

    public GridViewModel(IFileQueryService fileQueryService, IMessenger messenger)
        : base(messenger)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        Items = new ObservableCollection<FileSummaryDto>();
    }

    public ObservableCollection<FileSummaryDto> Items { get; }

    [ObservableProperty]
    private FileSummaryDto? selectedItem;

    [ObservableProperty]
    private string? searchText;

    [ObservableProperty]
    private int pageNumber = 1;

    [ObservableProperty]
    private int pageSize = 50;

    [ObservableProperty]
    private int totalCount;

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    [ObservableProperty]
    private bool showOnlyFavorites;

    [ObservableProperty]
    private bool showSearchHistory;

    public ObservableCollection<SearchFavoriteItem> Favorites { get; } = new();

    public ObservableCollection<SearchHistoryEntry> History { get; } = new();

    [RelayCommand(IncludeCancelCommand = true, AllowConcurrentExecutions = false)]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Načítám data...";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dto = new FileGridQueryDto
            {
                Text = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                Page = new PageRequest
                {
                    Page = PageNumber,
                    PageSize = PageSize,
                },
            };

            var result = await _fileQueryService.GetGridAsync(new FileGridQuery(dto), cancellationToken).ConfigureAwait(false);
            Items.Clear();
            foreach (var item in result.Items)
            {
                Items.Add(item);
            }

            TotalCount = result.TotalCount;
            StatusMessage = $"Načteno {Items.Count} z {result.TotalCount} položek.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Načítání zrušeno.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Načítání selhalo: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(TotalPages));
            PreviousPageCommand.NotifyCanExecuteChanged();
            NextPageCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private void PreviousPage()
    {
        if (PageNumber <= 1)
        {
            return;
        }

        PageNumber--;
        _ = RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private void NextPage()
    {
        if (PageNumber >= TotalPages)
        {
            return;
        }

        PageNumber++;
        _ = RefreshCommand.ExecuteAsync(null);
    }

    private bool CanGoToPreviousPage() => PageNumber > 1 && !IsBusy;

    private bool CanGoToNextPage() => PageNumber < TotalPages && !IsBusy;

    public async Task EnsureDataAsync(CancellationToken cancellationToken = default)
    {
        if (Items.Count == 0)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            await LoadAncillaryDataAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task LoadAncillaryDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            var favorites = await _fileQueryService.GetFavoritesAsync(cancellationToken).ConfigureAwait(false);
            Favorites.Clear();
            foreach (var favorite in favorites)
            {
                Favorites.Add(favorite);
            }

            var history = await _fileQueryService.GetSearchHistoryAsync(10, cancellationToken).ConfigureAwait(false);
            History.Clear();
            foreach (var entry in history.OrderByDescending(h => h.LastQueriedUtc))
            {
                History.Add(entry);
            }
        }
        catch
        {
            // ancillary data is best-effort
        }
    }

    public void Receive(GridRefreshMessage message)
    {
        if (message.Value.ForceReload)
        {
            _ = RefreshCommand.ExecuteAsync(null);
        }
        else
        {
            ScheduleRefresh();
        }
    }

    partial void OnSearchTextChanged(string? value)
    {
        PageNumber = 1;
        ScheduleRefresh();
    }

    partial void OnSelectedItemChanged(FileSummaryDto? value)
    {
        Messenger.Send(new SelectedFileChangedMessage(value?.Id));
    }

    partial void OnPageSizeChanged(int value)
    {
        if (value <= 0)
        {
            PageSize = 25;
            return;
        }

        PageNumber = 1;
        ScheduleRefresh();
        OnPropertyChanged(nameof(TotalPages));
    }

    partial void OnPageNumberChanged(int value)
    {
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    private void ScheduleRefresh()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_searchDebounce, token).ConfigureAwait(false);
                await RefreshCommand.ExecuteAsync(null).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }
}
// END CHANGE Veriado.WinUI/ViewModels/GridViewModel.cs
