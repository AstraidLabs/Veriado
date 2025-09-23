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
using Veriado.WinUI.ViewModels.Messages;

namespace Veriado.WinUI.ViewModels.Files;

public sealed partial class FilesGridViewModel : ViewModelBase
{
    private readonly IFileQueryService _queryService;

    [ObservableProperty]
    private string? searchText;

    [ObservableProperty]
    private bool isInfoBarOpen;

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

    public FilesGridViewModel(IMessenger messenger, IFileQueryService queryService)
        : base(messenger)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
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

        Messenger.Send(new OpenFileDetailMessage(id));
    }

    partial void OnStatusMessageChanged(string? value)
    {
        IsInfoBarOpen = !string.IsNullOrWhiteSpace(value);
    }
}
