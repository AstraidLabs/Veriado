using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.Mappers;
using Veriado.Models.Files;
using Veriado.Services;
using Veriado.Services.Files;
using Veriado.ViewModels.Base;

namespace Veriado.ViewModels.Files;

public sealed partial class FileDetailViewModel : ViewModelBase
{
    private readonly IFileQueryService _fileQueryService;
    private readonly INavigationService _navigationService;

    public FileDetailViewModel(IFileQueryService fileQueryService, INavigationService navigationService)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    }

    [ObservableProperty]
    private Guid? id;

    [ObservableProperty]
    private FileDetailModel? detail;

    [RelayCommand]
    private async Task LoadAsync(Guid fileId)
    {
        Id = fileId;

        await SafeExecuteAsync(async ct =>
        {
            var dto = await _fileQueryService.GetDetailAsync(fileId, ct).ConfigureAwait(false);
            if (dto is null)
            {
                Detail = null;
                HasError = true;
                StatusMessage = "Dokument nebyl nalezen.";
                return;
            }

            Detail = dto.ToFileDetailModel();
            StatusMessage = "Detail načten.";
        }, "Načítám detail…");
    }

    [RelayCommand]
    private void Close()
    {
        Cancel();
        _navigationService.GoBack();
    }
}
