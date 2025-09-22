using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using AutoMapper;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Contracts.Import;
using Veriado.Presentation.Messages;
using Veriado.Presentation.Models.Import;
using Veriado.Presentation.Services;
using Veriado.Services.Import;

namespace Veriado.Presentation.ViewModels;

/// <summary>
/// Coordinates the WinUI import workflow.
/// </summary>
public sealed partial class ImportViewModel : ViewModelBase
{
    private readonly IImportService _importService;
    private readonly IPickerService _pickerService;
    private readonly IMapper _mapper;
    private CancellationTokenSource? _importCancellationSource;

    public ImportViewModel(IImportService importService, IPickerService pickerService, IMapper mapper, IMessenger messenger)
        : base(messenger)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _pickerService = pickerService ?? throw new ArgumentNullException(nameof(pickerService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        FolderRequest.MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2);
    }

    [ObservableProperty]
    private bool isImporting;

    [ObservableProperty]
    private ImportFolderRequestModel folderRequest = new();

    [ObservableProperty]
    private ImportProgressModel progress = new();

    /// <summary>
    /// Prompts the user to pick a folder for import.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task BrowseFolderAsync(CancellationToken cancellationToken)
    {
        var folder = await _pickerService.PickFolderAsync(cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            FolderRequest.FolderPath = folder;
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

        if (string.IsNullOrWhiteSpace(FolderRequest.FolderPath))
        {
            LastError = "Vyberte složku k importu.";
            IsInfoBarOpen = true;
            return;
        }

        if (!Directory.Exists(FolderRequest.FolderPath))
        {
            LastError = $"Složka '{FolderRequest.FolderPath}' neexistuje.";
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
                    Progress.Processed = 0;
                    Progress.Total = 0;

                    FolderRequest.SearchPattern = string.IsNullOrWhiteSpace(FolderRequest.SearchPattern) ? "*" : FolderRequest.SearchPattern;
                    FolderRequest.MaxDegreeOfParallelism = Math.Max(1, FolderRequest.MaxDegreeOfParallelism);

                    var request = _mapper.Map<ImportFolderRequest>(FolderRequest);

                    var response = await _importService.ImportFolderAsync(request, token).ConfigureAwait(true);
                    if (!response.IsSuccess)
                    {
                        LastError = response.Errors.Count > 0 ? response.Errors[0].Message : "Import selhal.";
                        StatusMessage = "Import selhal.";
                        return;
                    }

                    var result = response.Data;
                    Progress.Processed = result?.Succeeded ?? 0;
                    Progress.Total = result?.Total ?? Progress.Processed;
                    StatusMessage = result is null
                        ? "Import dokončen."
                        : $"Import dokončen: {Progress.Processed}/{Progress.Total}.";

                    Messenger.Send(new ImportCompletedMessage(Progress.Total, Progress.Processed));
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
