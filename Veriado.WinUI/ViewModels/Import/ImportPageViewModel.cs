using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Import;
using Veriado.Services.Import;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Import;

public partial class ImportPageViewModel : ViewModelBase
{
    private readonly IImportService _importService;
    private readonly IHotStateService _hotStateService;
    private readonly IPickerService? _pickerService;
    private readonly AsyncRelayCommand _pickFolderCommand;
    private readonly AsyncRelayCommand _runImportCommand;

    public ImportPageViewModel(
        IImportService importService,
        IHotStateService hotStateService,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler,
        IPickerService? pickerService = null)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _hotStateService = hotStateService ?? throw new ArgumentNullException(nameof(hotStateService));
        _pickerService = pickerService;

        SelectedFolder = string.IsNullOrWhiteSpace(_hotStateService.LastFolder)
            ? null
            : _hotStateService.LastFolder;

        _pickFolderCommand = new AsyncRelayCommand(PickFolderAsync);
        _runImportCommand = new AsyncRelayCommand(RunImportAsync, CanRunImport);
    }

    [ObservableProperty]
    private string? selectedFolder;

    [ObservableProperty]
    private bool isImporting;

    public IAsyncRelayCommand PickFolderCommand => _pickFolderCommand;

    public IAsyncRelayCommand RunImportCommand => _runImportCommand;

    private bool CanRunImport() => !IsImporting && !string.IsNullOrWhiteSpace(SelectedFolder);

    private Task PickFolderAsync()
    {
        if (_pickerService is null)
        {
            StatusService.Error("Výběr složky není v této konfiguraci podporován.");
            return Task.CompletedTask;
        }

        return SafeExecuteAsync(async _ =>
        {
            var folder = await _pickerService.PickFolderAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                SelectedFolder = folder;
            }
        });
    }

    private Task RunImportAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFolder))
        {
            StatusService.Error("Vyberte složku pro import.");
            return Task.CompletedTask;
        }

        return SafeExecuteAsync(async cancellationToken =>
        {
            IsImporting = true;
            try
            {
                var request = new ImportFolderRequest
                {
                    FolderPath = SelectedFolder!,
                };

                var response = await _importService.ImportFolderAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.Success)
                {
                    var message = string.IsNullOrWhiteSpace(response.Message)
                        ? "Import se nezdařil."
                        : response.Message;
                    StatusService.Error(message);
                    return;
                }

                _hotStateService.LastFolder = SelectedFolder;
                StatusService.Info("Import dokončen.");
            }
            finally
            {
                IsImporting = false;
            }
        });
    }

    partial void OnSelectedFolderChanged(string? value)
    {
        _hotStateService.LastFolder = value;
        _runImportCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsImportingChanged(bool value)
    {
        _runImportCommand.NotifyCanExecuteChanged();
    }
}
