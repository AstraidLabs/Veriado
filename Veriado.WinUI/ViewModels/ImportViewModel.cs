using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Import;
using Veriado.Services.Import.Models;
using Veriado.WinUI.Messages;
using Veriado.WinUI.Services;

namespace Veriado.WinUI.ViewModels;

/// <summary>
/// Coordinates the WinUI import workflow.
/// </summary>
public sealed partial class ImportViewModel : ViewModelBase
{
    private readonly IImportService _importService;
    private readonly IPickerService _pickerService;
    private CancellationTokenSource? _importCancellationSource;

    public ImportViewModel(IImportService importService, IPickerService pickerService, IMessenger messenger)
        : base(messenger)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _pickerService = pickerService ?? throw new ArgumentNullException(nameof(pickerService));
    }

    [ObservableProperty]
    private bool isImporting;

    [ObservableProperty]
    private int processed;

    [ObservableProperty]
    private int total;

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

    /// <summary>
    /// Prompts the user to pick a folder for import.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task BrowseFolderAsync(CancellationToken cancellationToken)
    {
        var folder = await _pickerService.PickFolderAsync(cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            SelectedFolderPath = folder;
            LastError = null;
            StatusMessage = $"Vybrána složka {folder}.";
            IsInfoBarOpen = true;
        }
    }

    /// <summary>
    /// Invokes the folder import workflow through the orchestration service.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ImportAsync(CancellationToken cancellationToken)
    {
        if (IsImporting)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            LastError = "Vyberte složku k importu.";
            IsInfoBarOpen = true;
            return;
        }

        if (!Directory.Exists(SelectedFolderPath))
        {
            LastError = $"Složka '{SelectedFolderPath}' neexistuje.";
            IsInfoBarOpen = true;
            return;
        }

        _importCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = _importCancellationSource.Token;

        try
        {
            IsImporting = true;
            await SafeExecuteAsync(
                async token =>
                {
                    IsInfoBarOpen = true;
                    LastError = null;
                    StatusMessage = "Import byl spuštěn...";
                    Processed = 0;
                    Total = 0;

                    var request = new ImportFolderRequest
                    {
                        FolderPath = SelectedFolderPath!,
                        DefaultAuthor = DefaultAuthor,
                        ExtractContent = ExtractContent,
                        Recursive = Recursive,
                        SearchPattern = string.IsNullOrWhiteSpace(SearchPattern) ? "*" : SearchPattern,
                        MaxDegreeOfParallelism = Math.Max(1, MaxDegreeOfParallelism),
                    };

                    var response = await _importService.ImportFolderAsync(request, token).ConfigureAwait(true);
                    if (!response.IsSuccess)
                    {
                        LastError = response.Errors.Count > 0 ? response.Errors[0].Message : "Import selhal.";
                        StatusMessage = "Import selhal.";
                        return;
                    }

                    var result = response.Data;
                    Processed = result?.Succeeded ?? 0;
                    Total = result?.Total ?? Processed;
                    StatusMessage = result is null
                        ? "Import dokončen."
                        : $"Import dokončen: {Processed}/{Total}.";

                    Messenger.Send(new ImportCompletedMessage(Total, Processed));
                },
                "Import byl spuštěn...",
                cancellationToken: linkedToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Import byl zrušen.";
        }
        finally
        {
            IsImporting = false;
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

        TryCancelRunning();
    }

    /// <summary>
    /// Imports a single file described by the supplied request payload.
    /// </summary>
    public Task<ApiResponse<Guid>> ImportFileAsync(CreateFileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _importService.ImportFileAsync(request, cancellationToken);
    }
}
