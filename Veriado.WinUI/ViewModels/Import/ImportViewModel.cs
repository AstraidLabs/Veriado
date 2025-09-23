using System;
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
            return;
        }

        await SafeExecuteAsync(async ct =>
        {
            var request = new ImportFolderRequest
            {
                FolderPath = SelectedFolderPath!,
                DefaultAuthor = string.IsNullOrWhiteSpace(DefaultAuthor) ? null : DefaultAuthor,
                Recursive = Recursive,
                ExtractContent = ExtractContent,
                MaxDegreeOfParallelism = Math.Clamp(MaxDegreeOfParallelism, 1, 16),
            };

            var response = await _import.ImportFolderAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccess || response.Data is null)
            {
                HasError = true;
                StatusMessage = "Import se nezdařil.";
                return;
            }

            var result = response.Data;
            StatusMessage = $"Načteno: {result.Succeeded}/{result.Total}.";
        }, "Importuji dokumenty…");
    }

    [RelayCommand]
    private void CancelImport()
    {
        TryCancelRunning();
        StatusMessage = "Import byl zrušen.";
    }
}
