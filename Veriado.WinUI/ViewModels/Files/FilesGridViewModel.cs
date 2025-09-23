using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.Services;
using Veriado.ViewModels.Base;

namespace Veriado.ViewModels.Files;

public sealed partial class FilesGridViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;

    public FilesGridViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public ObservableCollection<FileListItemModel> Items { get; } = new();

    [ObservableProperty]
    private int createdDaysFrom;

    [ObservableProperty]
    private int createdDaysTo = 365;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await SafeExecuteAsync(async ct =>
        {
            await Task.Delay(150, ct).ConfigureAwait(false);

            Items.Clear();
            for (var i = 0; i < 9; i++)
            {
                Items.Add(new FileListItemModel
                {
                    Id = Guid.NewGuid(),
                    Name = $"Dokument {i + 1:00}"
                });
            }
        }, "Načítám položky…");

        StatusMessage = $"Načteno {Items.Count} položek.";
    }

    [RelayCommand]
    private void OpenDetail(Guid id)
    {
        _navigationService.NavigateToFileDetail(id);
    }
}

public sealed class FileListItemModel : ObservableObject
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
}
