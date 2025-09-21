// BEGIN CHANGE Veriado.WinUI/ViewModels/ImportViewModel.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Services.Import;
using Veriado.Services.Import.Models;
using Veriado.WinUI.Messages;
using Veriado.WinUI.Services;

namespace Veriado.WinUI.ViewModels;

/// <summary>
/// Coordinates folder and single file imports.
/// </summary>
public sealed partial class ImportViewModel : BaseViewModel
{
    private readonly IImportService _importService;
    private readonly IPickerService _pickerService;

    [ObservableProperty]
    private string? selectedFolderPath;

    [ObservableProperty]
    private string defaultAuthor = string.Empty;

    [ObservableProperty]
    private bool extractContent = true;

    [ObservableProperty]
    private bool recursive = true;

    [ObservableProperty]
    private int maxDegreeOfParallelism = Environment.ProcessorCount;

    [ObservableProperty]
    private string? searchPattern = "*";

    [ObservableProperty]
    private ImportBatchResult? lastResult;

    public ImportViewModel(IImportService importService, IPickerService pickerService, IMessenger messenger)
        : base(messenger)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _pickerService = pickerService ?? throw new ArgumentNullException(nameof(pickerService));
    }

    public string? LastResultSummary => LastResult is { } result
        ? $"Imported {result.Succeeded}/{result.Total} (failed: {result.Failed})"
        : null;

    [RelayCommand(IncludeCancelCommand = true, AllowConcurrentExecutions = false)]
    private async Task BrowseAsync(CancellationToken cancellationToken)
    {
        var folder = await _pickerService.PickFolderAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            SelectedFolderPath = folder;
        }
    }

    [RelayCommand(IncludeCancelCommand = true, AllowConcurrentExecutions = false)]
    private async Task ImportAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        var folder = SelectedFolderPath;
        if (string.IsNullOrWhiteSpace(folder))
        {
            StatusMessage = "Nejprve vyberte složku k importu.";
            return;
        }

        if (!Directory.Exists(folder))
        {
            StatusMessage = $"Složka '{folder}' neexistuje.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Import souborů byl spuštěn.";
        Messenger.Send(new ImportProgressMessage(ImportProgress.Started(StatusMessage)));

        try
        {
            var request = new ImportFolderRequest
            {
                FolderPath = folder,
                DefaultAuthor = DefaultAuthor,
                ExtractContent = ExtractContent,
                Recursive = Recursive,
                SearchPattern = string.IsNullOrWhiteSpace(SearchPattern) ? "*" : SearchPattern,
                MaxDegreeOfParallelism = Math.Max(1, MaxDegreeOfParallelism),
            };

            var response = await _importService.ImportFolderAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                StatusMessage = response.Errors.Count > 0
                    ? response.Errors[0].Message
                    : "Import selhal.";
                Messenger.Send(new ImportProgressMessage(ImportProgress.Failed(StatusMessage)));
                return;
            }

            LastResult = response.Data;
            StatusMessage = LastResult is { } result
                ? $"Import dokončen ({result.Succeeded}/{result.Total})"
                : "Import dokončen.";

            Messenger.Send(new ImportProgressMessage(ImportProgress.Completed(StatusMessage)));
            Messenger.Send(new GridRefreshMessage(new GridRefreshRequest(true)));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Import byl zrušen.";
            Messenger.Send(new ImportProgressMessage(ImportProgress.Failed(StatusMessage)));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import selhal: {ex.Message}";
            Messenger.Send(new ImportProgressMessage(ImportProgress.Failed(StatusMessage)));
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(LastResultSummary));
        }
    }

    partial void OnLastResultChanged(ImportBatchResult? value)
    {
        OnPropertyChanged(nameof(LastResultSummary));
    }

    partial void OnMaxDegreeOfParallelismChanged(int value)
    {
        if (value <= 0)
        {
            MaxDegreeOfParallelism = 1;
        }
    }
}
// END CHANGE Veriado.WinUI/ViewModels/ImportViewModel.cs
