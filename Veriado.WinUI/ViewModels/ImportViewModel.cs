using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.Services.Import;
using Veriado.Services.Import.Models;
using Veriado.WinUI.Services;

namespace Veriado.WinUI.ViewModels;

/// <summary>
/// Coordinates the WinUI import workflow.
/// </summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private readonly IImportService _importService;
    private readonly IPickerService _pickerService;
    private CancellationTokenSource? _importCancellationSource;

    public ImportViewModel(IImportService importService, IPickerService pickerService)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _pickerService = pickerService ?? throw new ArgumentNullException(nameof(pickerService));
    }

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? selectedFolderPath;

    [ObservableProperty]
    private string? defaultAuthor;

    [ObservableProperty]
    private bool extractContent = true;

    [ObservableProperty]
    private bool recursive = true;

    [ObservableProperty]
    private int maxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2);

    [ObservableProperty]
    private string searchPattern = "*";

    [ObservableProperty]
    private ImportBatchResult? lastResult;

    [ObservableProperty]
    private bool showInfoBar;

    /// <summary>
    /// Prompts the user to pick a folder for import.
    /// </summary>
    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        var folder = await _pickerService.PickFolderAsync(CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            SelectedFolderPath = folder;
        }
    }

    /// <summary>
    /// Invokes the folder import workflow through the orchestration service.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ImportAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            StatusMessage = "Vyberte složku k importu.";
            ShowInfoBar = true;
            return;
        }

        if (!Directory.Exists(SelectedFolderPath))
        {
            StatusMessage = $"Složka '{SelectedFolderPath}' neexistuje.";
            ShowInfoBar = true;
            return;
        }

        _importCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = _importCancellationSource.Token;

        try
        {
            IsBusy = true;
            ShowInfoBar = true;
            StatusMessage = "Import byl spuštěn...";

            var request = new ImportFolderRequest
            {
                FolderPath = SelectedFolderPath!,
                DefaultAuthor = DefaultAuthor,
                ExtractContent = ExtractContent,
                Recursive = Recursive,
                SearchPattern = string.IsNullOrWhiteSpace(SearchPattern) ? "*" : SearchPattern,
                MaxDegreeOfParallelism = Math.Max(1, MaxDegreeOfParallelism),
            };

            var response = await _importService.ImportFolderAsync(request, linkedToken);
            if (!response.IsSuccess)
            {
                StatusMessage = response.Errors.Count > 0 ? response.Errors[0].Message : "Import selhal.";
                return;
            }

            LastResult = response.Data;
            StatusMessage = LastResult is { } result
                ? $"Import dokončen: {result.Succeeded}/{result.Total}"
                : "Import dokončen.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Import byl zrušen.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import selhal: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _importCancellationSource?.Dispose();
            _importCancellationSource = null;
        }
    }

    /// <summary>
    /// Cancels the running import operation, if any.
    /// </summary>
    [RelayCommand]
    private void CancelImport()
    {
        if (_importCancellationSource is null)
        {
            return;
        }

        if (!_importCancellationSource.IsCancellationRequested)
        {
            _importCancellationSource.Cancel();
        }
    }
}
