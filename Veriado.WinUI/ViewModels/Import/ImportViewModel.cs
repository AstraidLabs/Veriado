using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.Contracts.Import;
using Veriado.Models.Import;
using Veriado.Presentation.Services;
using Veriado.Services.Import;
using Veriado.ViewModels.Base;

namespace Veriado.ViewModels.Import;

public sealed partial class ImportViewModel : ViewModelBase
{
    private readonly IImportService _importService;
    private readonly IPickerService _pickerService;

    public ImportViewModel(IImportService importService, IPickerService pickerService)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _pickerService = pickerService ?? throw new ArgumentNullException(nameof(pickerService));
        progress = new ImportProgressModel();
    }

    [ObservableProperty]
    private string? selectedFolderPath;

    [ObservableProperty]
    private string? defaultAuthor;

    [ObservableProperty]
    private string searchPattern = "*.*";

    [ObservableProperty]
    private bool recursive = true;

    [ObservableProperty]
    private bool extractContent = true;

    [ObservableProperty]
    private int maxDegreeOfParallelism = 2;

    [ObservableProperty]
    private ImportProgressModel progress;

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        await SafeExecuteAsync(async ct =>
        {
            var folder = await _pickerService.PickFolderAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                SelectedFolderPath = folder;
                StatusMessage = $"Vybrána složka: {folder}.";
            }
        }, "Otevírám výběr složky…");
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            HasError = true;
            StatusMessage = "Vyberte prosím složku k importu.";
            return;
        }

        await SafeExecuteAsync(async ct =>
        {
            ResetProgress();
            Progress.IsRunning = true;
            Progress.CurrentPath = SelectedFolderPath;

            try
            {
                var request = new ImportFolderRequest
                {
                    FolderPath = SelectedFolderPath!,
                    DefaultAuthor = string.IsNullOrWhiteSpace(DefaultAuthor) ? null : DefaultAuthor,
                    ExtractContent = ExtractContent,
                    MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                    SearchPattern = string.IsNullOrWhiteSpace(SearchPattern) ? "*.*" : SearchPattern,
                    Recursive = Recursive,
                };

                var response = await _importService.ImportFolderAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccess || response.Data is null)
                {
                    HasError = true;
                    StatusMessage = response.Errors.Count > 0
                        ? string.Join(Environment.NewLine, response.Errors.Select(e => e.Message))
                        : "Import selhal.";
                    return;
                }

                var result = response.Data;
                Progress.Total = result.Total;
                Progress.Processed = result.Succeeded + result.Failed;
                Progress.ErrorsCount = result.Failed;

                HasError = result.Errors.Count > 0;
                StatusMessage = HasError
                    ? $"Import dokončen s {result.Errors.Count} chybami."
                    : $"Import dokončen. Zpracováno {result.Succeeded} z {result.Total} souborů.";
            }
            finally
            {
                Progress.IsRunning = false;
                Progress.CurrentPath = null;
            }
        }, "Importuji soubory…");
    }

    [RelayCommand]
    private void Reset()
    {
        Cancel();
        Progress = new ImportProgressModel();
        StatusMessage = null;
        HasError = false;
    }

    private void ResetProgress()
    {
        Progress.Total = 0;
        Progress.Processed = 0;
        Progress.ErrorsCount = 0;
        Progress.CurrentPath = null;
        Progress.IsRunning = false;
    }
}
