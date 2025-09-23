using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Contracts.Search;
using Veriado.Services.Files;
using Veriado.WinUI.ViewModels.Base;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.Views;

namespace Veriado.WinUI.ViewModels.Files;

public sealed partial class FilesGridViewModel : ViewModelBase
{
    private readonly IFileQueryService _queryService;
    private readonly INavigationService _navigationService;
    private readonly Func<FileDetailView> _detailViewFactory;

    [ObservableProperty]
    private string? searchText;

    [ObservableProperty]
    private int searchModeIndex;

    [ObservableProperty]
    private DateTimeOffset? createdFrom;

    [ObservableProperty]
    private DateTimeOffset? createdTo;

    [ObservableProperty]
    private double? lowerValue;

    [ObservableProperty]
    private double? upperValue;

    public ObservableCollection<FileSummaryDto> Items { get; } = new();

    public ObservableCollection<string> SearchSuggestions { get; } = new();

    public ObservableCollection<string> QueryTokens { get; } = new();

    public ObservableCollection<SearchFavoriteItem> Favorites { get; } = new();

    public ObservableCollection<SearchHistoryEntry> History { get; } = new();

    public FilesGridViewModel(
        IMessenger messenger,
        IStatusService statusService,
        IFileQueryService queryService,
        INavigationService navigationService,
        Func<FileDetailView> detailViewFactory)
        : base(messenger, statusService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _detailViewFactory = detailViewFactory ?? throw new ArgumentNullException(nameof(detailViewFactory));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await SafeExecuteAsync(async ct =>
        {
            var page = await _queryService.GetGridAsync(new FileGridQueryDto
            {
                Text = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                Page = new PageRequest
                {
                    Page = 1,
                    PageSize = 50,
                },
            }, ct).ConfigureAwait(false);

            Items.Clear();
            foreach (var item in page.Items)
            {
                Items.Add(item);
            }

            StatusMessage = Items.Count == 0
                ? "Žádné dokumenty neodpovídají aktuálnímu filtru."
                : $"Načteno {Items.Count} dokumentů.";
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
        _navigationService.NavigateToDetail(view);

        if (view.DataContext is FileDetailViewModel detailViewModel)
        {
            _ = detailViewModel.LoadCommand.ExecuteAsync(id);
        }
    }

}
