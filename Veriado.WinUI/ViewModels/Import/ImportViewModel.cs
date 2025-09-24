using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Import;
using Veriado.Services.Abstractions;
using Veriado.Services.Import;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Import;

public sealed partial class ImportViewModel : ViewModelBase
{
    private readonly IImportService _import;
    private readonly IPickerService _picker;
    private readonly IHotStateService _hotState;

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
    private string? lastError;

    [ObservableProperty]
    private bool isImporting;

    [ObservableProperty]
    private string? searchPattern;

    [ObservableProperty]
    private int processed;

    [ObservableProperty]
    private int total;

    [RelayCommand]
    private void UseFolderPath(string? folderPath)
    {
        if (IsBusy || IsImporting || string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        SelectedFolderPath = folderPath;
        LastError = null;
        HasError = false;
        StatusService.Clear();
    }

    public ImportViewModel(
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler,
        IImportService import,
        IPickerService picker,
        IHotStateService hotState)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _import = import ?? throw new ArgumentNullException(nameof(import));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _hotState = hotState ?? throw new ArgumentNullException(nameof(hotState));

        SelectedFolderPath = _hotState.LastFolder;
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        await SafeExecuteAsync(async _ =>
        {
            var folder = await _picker.PickFolderAsync();
            if (!string.IsNullOrWhiteSpace(folder))
            {
                SelectedFolderPath = folder;
                LastError = null;
                StatusService.Clear();
            }
        });
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            HasError = true;
            var message = "Vyberte složku pro import.";
            StatusService.Error(message);
            LastError = message;
            return;
        }

        IsImporting = true;
        Processed = 0;
        Total = 0;
        LastError = null;

        try
        {
            await SafeExecuteAsync(async ct =>
            {
                var request = new ImportFolderRequest
                {
                    FolderPath = SelectedFolderPath!,
                    DefaultAuthor = string.IsNullOrWhiteSpace(DefaultAuthor) ? null : DefaultAuthor,
                    Recursive = Recursive,
                    ExtractContent = ExtractContent,
                    MaxDegreeOfParallelism = Math.Clamp(MaxDegreeOfParallelism, 1, 16),
                    SearchPattern = string.IsNullOrWhiteSpace(SearchPattern) ? null : SearchPattern,
                };

                var response = await _import.ImportFolderAsync(request, ct);
                if (!response.IsSuccess || response.Data is null)
                {
                    HasError = true;
                    StatusService.Error("Import se nezdařil.");
                    LastError = response.Errors.Count > 0
                        ? string.Join(Environment.NewLine, response.Errors.Select(error => error.Message))
                        : "Import se nezdařil.";
                    return;
                }

                var result = response.Data;
                Processed = result.Succeeded;
                Total = result.Total;

                if (result.Errors.Count > 0)
                {
                    HasError = true;
                    LastError = string.Join(Environment.NewLine, result.Errors.Select(error => error.Message));
                    StatusService.Error($"Import dokončen s chybami ({result.Succeeded}/{result.Total}).");
                }
                else
                {
                    HasError = false;
                    StatusService.Info($"Import dokončen ({result.Succeeded}/{result.Total}).");
                }
            }, "Importuji dokumenty…");
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private void CancelImport()
    {
        TryCancelRunning();
        StatusService.Info("Import byl zrušen.");
        LastError = null;
        IsImporting = false;
    }

    partial void OnSelectedFolderPathChanged(string? value)
    {
        _hotState.LastFolder = string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
