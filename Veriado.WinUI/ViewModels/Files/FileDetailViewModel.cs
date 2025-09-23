using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.Services;
using Veriado.ViewModels.Base;

namespace Veriado.ViewModels.Files;

public sealed partial class FileDetailViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;

    public FileDetailViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    [ObservableProperty]
    private Guid fileId;

    [ObservableProperty]
    private string? fileName;

    [RelayCommand]
    private async Task LoadAsync(Guid id)
    {
        FileId = id;
        await SafeExecuteAsync(async ct =>
        {
            await Task.Delay(120, ct).ConfigureAwait(false);
            FileName = $"Detail dokumentu {id.ToString()[..8]}";
        }, "Načítám detail…");

        StatusMessage = "Detail načten.";
    }

    [RelayCommand]
    private void Close()
    {
        _navigationService.GoBack();
    }
}
