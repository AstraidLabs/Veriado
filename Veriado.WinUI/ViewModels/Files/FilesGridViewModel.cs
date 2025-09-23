using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Files;

namespace Veriado.WinUI.ViewModels.Files;

public partial class FilesGridViewModel : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly IFileQueryService _queryService;

    [ObservableProperty]
    private string? searchText;

    public ObservableCollection<FileSummaryDto> Items { get; } = new();

    public FilesGridViewModel(IMessenger messenger, IFileQueryService queryService)
    {
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var page = await _queryService.GetGridAsync(new FileGridQueryDto
        {
            Text = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
            Page = new PageRequest
            {
                Page = 1,
                PageSize = 50,
            },
        }, CancellationToken.None).ConfigureAwait(false);

        Items.Clear();
        foreach (var item in page.Items)
        {
            Items.Add(item);
        }
    }

    [RelayCommand]
    private void OpenDetail(Guid id)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        _messenger.Send(new FileSelectedMessage(id));
    }
}
