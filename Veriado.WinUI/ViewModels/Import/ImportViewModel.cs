using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.Contracts.Import;
using Veriado.Services.Import;
using Veriado.WinUI.Services.Pickers;

namespace Veriado.WinUI.ViewModels.Import;

public partial class ImportViewModel : ObservableObject
{
    private readonly IImportService _import;
    private readonly IPickerService _picker;

    [ObservableProperty]
    private string? selectedFolderPath;

    [ObservableProperty]
    private string? defaultAuthor;

    [ObservableProperty]
    private bool recursive = true;

    [ObservableProperty]
    private bool extractContent = true;

    [ObservableProperty]
    private int maxDegreeOfParallelism = 4;

    [ObservableProperty]
    private bool isImporting;

    [ObservableProperty]
    private bool isInfoBarOpen;

    [ObservableProperty]
    private string? statusMessage;

    public ImportViewModel(IImportService import, IPickerService picker)
    {
        _import = import ?? throw new ArgumentNullException(nameof(import));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        SelectedFolderPath = await _picker.PickFolderAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            StatusMessage = "Vyberte složku pro import.";
            IsInfoBarOpen = true;
            return;
        }

        IsImporting = true;
        try
        {
            var request = new ImportFolderRequest
            {
                FolderPath = SelectedFolderPath!,
                DefaultAuthor = string.IsNullOrWhiteSpace(DefaultAuthor) ? null : DefaultAuthor,
                Recursive = Recursive,
                ExtractContent = ExtractContent,
                MaxDegreeOfParallelism = Math.Clamp(MaxDegreeOfParallelism, 1, 16),
            };

            var response = await _import.ImportFolderAsync(request, CancellationToken.None).ConfigureAwait(false);
            if (!response.IsSuccess || response.Data is null)
            {
                StatusMessage = "Import se nezdařil.";
                IsInfoBarOpen = true;
                return;
            }

            var result = response.Data;
            StatusMessage = $"Načteno: {result.Succeeded}/{result.Total}.";
            IsInfoBarOpen = true;
        }
        finally
        {
            IsImporting = false;
        }
    }
}
