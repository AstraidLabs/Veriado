using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Search;
using Veriado.Services.Files;
using Veriado.WinUI.ViewModels.Base;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.ViewModels.Search;

public sealed partial class HistoryViewModel : ViewModelBase
{
    private readonly IFileQueryService _fileQueryService;

    public HistoryViewModel(IMessenger messenger, IStatusService statusService, IFileQueryService fileQueryService)
        : base(messenger, statusService)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
    }

    public ObservableCollection<SearchHistoryEntry> Items { get; } = new();

    [ObservableProperty]
    private int take = 50;

    [RelayCommand]
    private async Task LoadAsync()
    {
        await SafeExecuteAsync(async ct =>
        {
            var entries = await _fileQueryService.GetSearchHistoryAsync(Take, ct).ConfigureAwait(false);

            Items.Clear();
            foreach (var entry in entries)
            {
                Items.Add(entry);
            }

            StatusMessage = Items.Count == 0
                ? "Historie je prázdná."
                : $"Načteno {Items.Count} položek historie.";
        }, "Načítám historii hledání…");
    }
}
