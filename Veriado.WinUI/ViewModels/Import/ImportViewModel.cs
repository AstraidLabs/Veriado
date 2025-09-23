using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Import;
using Veriado.Services.Import;
using Veriado.WinUI.Services.Pickers;
using Veriado.WinUI.Services.Windowing;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Import;

public sealed partial class ImportViewModel : ViewModelBase
{
    private readonly IImportService _import;
    private readonly IPickerService _picker;
    private readonly IWindowProvider _windowProvider;

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
    private bool isInfoBarOpen;

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

    public ImportViewModel(
        IMessenger messenger,
        IImportService import,
        IPickerService picker,
        IWindowProvider windowProvider)
        : base(messenger)
    {
        _import = import ?? throw new ArgumentNullException(nameof(import));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        var window = _windowProvider.GetMainWindow();
        await SafeExecuteAsync(async _ =>
        {
            var folder = await _picker.PickFolderAsync(window).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                SelectedFolderPath = folder;
                StatusMessage = null;
                LastError = null;
            }
        });
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            HasError = true;
            StatusMessage = "Vyberte složku pro import.";
            LastError = StatusMessage;
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

                var response = await _import.ImportFolderAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccess || response.Data is null)
                {
                    HasError = true;
                    StatusMessage = "Import se nezdařil.";
                    LastError = response.Errors.Count > 0
                        ? string.Join(Environment.NewLine, response.Errors.Select(error => error.Message))
                        : StatusMessage;
                    return;
                }

                var result = response.Data;
                Processed = result.Succeeded;
                Total = result.Total;

                if (result.Errors.Count > 0)
                {
                    HasError = true;
                    LastError = string.Join(Environment.NewLine, result.Errors.Select(error => error.Message));
                    StatusMessage = $"Import dokončen s chybami ({result.Succeeded}/{result.Total}).";
                }
                else
                {
                    HasError = false;
                    StatusMessage = $"Import dokončen ({result.Succeeded}/{result.Total}).";
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
        StatusMessage = "Import byl zrušen.";
        LastError = null;
        IsImporting = false;
    }

    partial void OnStatusMessageChanged(string? value)
    {
        IsInfoBarOpen = !string.IsNullOrWhiteSpace(value);
    }
}
