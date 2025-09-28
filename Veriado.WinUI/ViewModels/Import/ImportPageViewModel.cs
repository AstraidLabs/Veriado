using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Common;
using Veriado.Contracts.Import;
using Veriado.Services.Import;
using Veriado.WinUI.Models.Import;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Import;

public partial class ImportPageViewModel : ViewModelBase
{
    private const int MaxLogEntries = 250;

    private readonly IImportService _importService;
    private readonly IHotStateService? _hotStateService;
    private readonly IPickerService? _pickerService;
    private readonly AsyncRelayCommand _pickFolderCommand;
    private readonly AsyncRelayCommand _runImportCommand;
    private readonly RelayCommand _stopImportCommand;
    private readonly RelayCommand _clearResultsCommand;
    private readonly RelayCommand<ImportErrorItem> _openErrorDetailCommand;
    private CancellationTokenSource? _importCancellation;

    private int _okCount;
    private int _errorCount;
    private int _skipCount;

    public ImportPageViewModel(
        IImportService importService,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler,
        IHotStateService? hotStateService = null,
        IPickerService? pickerService = null)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _hotStateService = hotStateService;
        _pickerService = pickerService;

        Log = new ObservableCollection<ImportLogItem>();
        Errors = new ObservableCollection<ImportErrorItem>();
        Log.CollectionChanged += OnLogCollectionChanged;
        Errors.CollectionChanged += OnErrorsCollectionChanged;

        _pickFolderCommand = new AsyncRelayCommand(ExecutePickFolderAsync, () => !IsImporting);
        _runImportCommand = new AsyncRelayCommand(ExecuteRunImportAsync, CanRunImport);
        _stopImportCommand = new RelayCommand(ExecuteStopImport, () => IsImporting);
        _clearResultsCommand = new RelayCommand(ExecuteClearResults, CanClearResults);
        _openErrorDetailCommand = new RelayCommand<ImportErrorItem>(ExecuteOpenErrorDetail);

        RestoreStateFromHotStorage();
        PopulateDefaultAuthorFromCurrentUser();
    }

    [ObservableProperty]
    private string? selectedFolder;

    [ObservableProperty]
    private bool recursive = true;

    [ObservableProperty]
    private bool keepFsMetadata = true;

    [ObservableProperty]
    private bool setReadOnly;

    [ObservableProperty]
    private bool useParallel = true;

    [ObservableProperty]
    private int maxDegreeOfParallelism = Environment.ProcessorCount;

    [ObservableProperty]
    private string? defaultAuthor;

    [ObservableProperty]
    private double maxFileSizeMegabytes;

    [ObservableProperty]
    private bool isImporting;

    [ObservableProperty]
    private bool isIndeterminate;

    [ObservableProperty]
    private int processed;

    [ObservableProperty]
    private int total;

    public ObservableCollection<ImportLogItem> Log { get; }

    public ObservableCollection<ImportErrorItem> Errors { get; }

    public int OkCount
    {
        get => _okCount;
        private set
        {
            if (SetProperty(ref _okCount, value))
            {
                OnPropertyChanged(nameof(ProgressText));
                _clearResultsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int ErrorCount
    {
        get => _errorCount;
        private set
        {
            if (SetProperty(ref _errorCount, value))
            {
                OnPropertyChanged(nameof(ProgressText));
                _clearResultsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int SkipCount
    {
        get => _skipCount;
        private set
        {
            if (SetProperty(ref _skipCount, value))
            {
                OnPropertyChanged(nameof(ProgressText));
                _clearResultsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ProgressText =>
        $"Zpracováno {Processed}/{(Total > 0 ? Total : 0)} • OK {OkCount} • Chyby {ErrorCount} • Skip {SkipCount}";

    public bool HasErrors => Errors.Count > 0;

    public IAsyncRelayCommand PickFolderCommand => _pickFolderCommand;

    public IAsyncRelayCommand RunImportCommand => _runImportCommand;

    public IRelayCommand StopImportCommand => _stopImportCommand;

    public IRelayCommand ClearResultsCommand => _clearResultsCommand;

    public IRelayCommand<ImportErrorItem> OpenErrorDetailCommand => _openErrorDetailCommand;

    private bool CanRunImport() => !IsImporting && !string.IsNullOrWhiteSpace(SelectedFolder);

    private bool CanClearResults() => Log.Count > 0 || Errors.Count > 0 || Processed > 0 || Total > 0 || OkCount > 0 || ErrorCount > 0 || SkipCount > 0;

    private Task ExecutePickFolderAsync()
    {
        if (_pickerService is null)
        {
            StatusService.Info("Výběr složky není v této konfiguraci podporován.");
            return Task.CompletedTask;
        }

        return SafeExecuteAsync(async _ =>
        {
            var folder = await _pickerService.PickFolderAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                await Dispatcher.Enqueue(() => SelectedFolder = folder);
            }
        }, "Otevírám dialog výběru složky...");
    }

    private Task ExecuteRunImportAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFolder))
        {
            StatusService.Error("Vyberte složku pro import.");
            return Task.CompletedTask;
        }

        return SafeExecuteAsync(RunImportInternalAsync, "Importuji…");
    }

    private void ExecuteStopImport()
    {
        _importCancellation?.Cancel();
        TryCancelRunning();
    }

    private void ExecuteClearResults()
    {
        Log.Clear();
        Errors.Clear();
        Processed = 0;
        Total = 0;
        OkCount = 0;
        ErrorCount = 0;
        SkipCount = 0;
        IsIndeterminate = false;
        StatusService.Info("Výsledky importu byly vymazány.");
    }

    private void ExecuteOpenErrorDetail(ImportErrorItem? item)
    {
        if (item is null)
        {
            return;
        }

        // TODO: Implement navigation to import error detail page.
    }

    private async Task RunImportInternalAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SelectedFolder))
        {
            StatusService.Error("Vyberte složku pro import.");
            return;
        }

        _importCancellation?.Dispose();
        _importCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await Dispatcher.Enqueue(() =>
            {
                IsImporting = true;
                IsIndeterminate = true;
            });

            await ResetProgressAsync().ConfigureAwait(false);
            await AddLogAsync(
                    "Import",
                    $"Spouštím import ze složky '{SelectedFolder}'.",
                    "info",
                    string.IsNullOrWhiteSpace(SelectedFolder) ? null : $"Složka: {SelectedFolder}")
                .ConfigureAwait(false);

            var request = BuildRequest();
            var options = BuildImportOptions();
            var folderPath = request.FolderPath;

            var statusReported = false;

            try
            {
                statusReported = await ProcessStreamingAsync(folderPath, options, _importCancellation.Token).ConfigureAwait(false);
            }
            catch (NotSupportedException)
            {
                statusReported = await ProcessBatchImportAsync(request, _importCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await AddLogAsync("Import", "Import byl zrušen uživatelem.", "warning").ConfigureAwait(false);
                StatusService.Info("Import byl zrušen.");
                return;
            }

            await Dispatcher.Enqueue(() => IsIndeterminate = false);

            if (!statusReported)
            {
                StatusService.Info("Import dokončen.");
            }
        }
        finally
        {
            _importCancellation?.Dispose();
            _importCancellation = null;
            await Dispatcher.Enqueue(() =>
            {
                IsImporting = false;
                IsIndeterminate = false;
            });
        }
    }

    private ImportFolderRequest BuildRequest()
    {
        var maxParallel = UseParallel
            ? Math.Max(1, MaxDegreeOfParallelism)
            : 1;

        return new ImportFolderRequest
        {
            FolderPath = SelectedFolder!.Trim(),
            Recursive = Recursive,
            KeepFsMetadata = KeepFsMetadata,
            SetReadOnly = SetReadOnly,
            MaxDegreeOfParallelism = maxParallel,
            DefaultAuthor = string.IsNullOrWhiteSpace(DefaultAuthor) ? null : DefaultAuthor,
            MaxFileSizeBytes = CalculateMaxFileSizeBytes(),
        };
    }

    private ImportOptions BuildImportOptions()
    {
        var maxParallel = UseParallel
            ? Math.Max(1, MaxDegreeOfParallelism)
            : 1;

        return new ImportOptions
        {
            MaxFileSizeBytes = CalculateMaxFileSizeBytes(),
            MaxDegreeOfParallelism = maxParallel,
        };
    }

    private long? CalculateMaxFileSizeBytes()
    {
        if (MaxFileSizeMegabytes <= 0)
        {
            return null;
        }

        var bytes = MaxFileSizeMegabytes * 1024d * 1024d;
        if (double.IsNaN(bytes) || double.IsInfinity(bytes))
        {
            return null;
        }

        if (bytes >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)Math.Round(bytes, MidpointRounding.AwayFromZero);
    }

    private async Task<bool> ProcessStreamingAsync(string folderPath, ImportOptions options, CancellationToken cancellationToken)
    {
        var statusReported = false;

        await foreach (var progress in _importService.ImportFolderStreamAsync(folderPath, options, cancellationToken).ConfigureAwait(false))
        {
            await HandleProgressEventAsync(progress).ConfigureAwait(false);
            if (progress.Kind == ImportProgressKind.BatchCompleted)
            {
                statusReported = true;
            }
        }

        return statusReported;
    }

    private async Task HandleProgressEventAsync(ImportProgressEvent progress)
    {
        switch (progress.Kind)
        {
            case ImportProgressKind.BatchStarted:
                await HandleBatchStartedAsync(progress).ConfigureAwait(false);
                break;
            case ImportProgressKind.Progress:
            case ImportProgressKind.FileStarted:
                await HandleProgressSnapshotAsync(progress).ConfigureAwait(false);
                break;
            case ImportProgressKind.FileCompleted:
                await HandleProgressSnapshotAsync(progress).ConfigureAwait(false);
                await HandleFileCompletedAsync(progress).ConfigureAwait(false);
                break;
            case ImportProgressKind.Error:
                await HandleProgressSnapshotAsync(progress).ConfigureAwait(false);
                await HandleErrorEventAsync(progress).ConfigureAwait(false);
                break;
            case ImportProgressKind.BatchCompleted:
                await HandleBatchCompletedAsync(progress).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleBatchStartedAsync(ImportProgressEvent progress)
    {
        await Dispatcher.Enqueue(() =>
        {
            Total = progress.TotalFiles ?? 0;
            Processed = 0;
            OkCount = 0;
            ErrorCount = 0;
            SkipCount = 0;
            IsIndeterminate = !progress.TotalFiles.HasValue || progress.TotalFiles.Value <= 0;
        });

        var message = string.IsNullOrWhiteSpace(progress.Message)
            ? "Import byl spuštěn."
            : progress.Message;
        await AddLogAsync("Import", message, "info").ConfigureAwait(false);
    }

    private Task HandleProgressSnapshotAsync(ImportProgressEvent progress)
    {
        return Dispatcher.Enqueue(() =>
        {
            var currentTotal = progress.TotalFiles ?? Total;
            var currentProcessed = progress.ProcessedFiles ?? Processed;
            var currentSucceeded = progress.SucceededFiles ?? OkCount;
            var currentFailed = progress.FailedFiles ?? ErrorCount;

            Total = currentTotal;
            Processed = currentProcessed;
            OkCount = currentSucceeded;
            ErrorCount = currentFailed;
            SkipCount = Math.Max(0, currentTotal - currentSucceeded - currentFailed);

            if (progress.TotalFiles.HasValue)
            {
                IsIndeterminate = currentTotal <= 0;
            }
        });
    }

    private Task HandleFileCompletedAsync(ImportProgressEvent progress)
    {
        var fileName = string.IsNullOrWhiteSpace(progress.FilePath)
            ? "Soubor"
            : Path.GetFileName(progress.FilePath);
        var detail = string.IsNullOrWhiteSpace(progress.FilePath) ? null : progress.FilePath;

        return AddLogAsync("OK", fileName, "success", detail);
    }

    private async Task HandleErrorEventAsync(ImportProgressEvent progress)
    {
        if (progress.Error is null)
        {
            return;
        }

        var item = CreateErrorItem(progress.Error);

        await Dispatcher.Enqueue(() =>
        {
            Errors.Add(item);
        });

        var detailBuilder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(progress.Error.FilePath))
        {
            detailBuilder.AppendLine($"Soubor: {progress.Error.FilePath}");
        }

        if (!string.IsNullOrWhiteSpace(progress.Error.Suggestion))
        {
            detailBuilder.Append(progress.Error.Suggestion);
        }

        var detail = detailBuilder.Length > 0 ? detailBuilder.ToString() : null;
        await AddLogAsync("Chyba", progress.Error.Message, "error", detail).ConfigureAwait(false);
    }

    private async Task HandleBatchCompletedAsync(ImportProgressEvent progress)
    {
        await HandleProgressSnapshotAsync(progress).ConfigureAwait(false);

        var aggregate = progress.Aggregate ?? ImportAggregateResult.EmptySuccess;

        await Dispatcher.Enqueue(() =>
        {
            SkipCount = Math.Max(0, aggregate.Total - aggregate.Succeeded - aggregate.Failed);
            IsIndeterminate = false;
        });

        await EnsureAggregateErrorsAsync(aggregate).ConfigureAwait(false);

        var result = new ImportBatchResult(
            aggregate.Status,
            aggregate.Total,
            aggregate.Succeeded,
            aggregate.Failed,
            aggregate.Errors);

        var summary = aggregate.Status switch
        {
            ImportBatchStatus.Success => $"Import dokončen. Úspěšně importováno {aggregate.Succeeded} z {aggregate.Total} souborů.",
            ImportBatchStatus.PartialSuccess => $"Import dokončen s částečným úspěchem ({aggregate.Succeeded}/{aggregate.Total}). Zkontrolujte prosím chyby.",
            ImportBatchStatus.Failure => "Import se nezdařil. Zkontrolujte chyby.",
            ImportBatchStatus.FatalError => "Import byl zastaven kvůli fatální chybě. Opravte problém a zkuste to znovu.",
            _ => "Import dokončen.",
        };

        var logStatus = aggregate.Status switch
        {
            ImportBatchStatus.Success => "success",
            ImportBatchStatus.PartialSuccess => "warning",
            ImportBatchStatus.Failure => "error",
            ImportBatchStatus.FatalError => "error",
            _ => "info",
        };

        await AddLogAsync("Výsledek", summary, logStatus, BuildResultDetail(result)).ConfigureAwait(false);

        switch (aggregate.Status)
        {
            case ImportBatchStatus.Success:
                StatusService.Info(summary);
                break;
            case ImportBatchStatus.PartialSuccess:
            case ImportBatchStatus.Failure:
            case ImportBatchStatus.FatalError:
                StatusService.Error(summary);
                break;
            default:
                StatusService.Info(summary);
                break;
        }
    }

    private async Task EnsureAggregateErrorsAsync(ImportAggregateResult aggregate)
    {
        if (aggregate.Errors.Count == 0)
        {
            return;
        }

        await Dispatcher.Enqueue(() =>
        {
            var existing = new HashSet<string>(Errors.Select(static error => error.UniqueKey));
            foreach (var error in aggregate.Errors)
            {
                var item = CreateErrorItem(error);
                if (!existing.Add(item.UniqueKey))
                {
                    continue;
                }

                Errors.Add(item);
            }
        });
    }

    private ImportErrorItem CreateErrorItem(ImportError error)
    {
        var fileName = string.IsNullOrWhiteSpace(error.FilePath)
            ? "Soubor"
            : Path.GetFileName(error.FilePath);

        return new ImportErrorItem(
            fileName,
            error.Message,
            null,
            error.Code,
            error.Suggestion,
            error.Timestamp,
            error.FilePath,
            error.StackTrace);
    }

    private async Task<bool> ProcessBatchImportAsync(ImportFolderRequest request, CancellationToken cancellationToken)
    {
        var response = await _importService.ImportFolderAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Data is null)
        {
            var message = ExtractErrorMessage(response);
            await AddLogAsync("Import selhal", message, "error").ConfigureAwait(false);
            StatusService.Error(message);
            return true;
        }

        var result = response.Data;

        await Dispatcher.Enqueue(() =>
        {
            Total = result.Total;
            Processed = result.Total;
            OkCount = result.Succeeded;
            ErrorCount = result.Failed;
            SkipCount = Math.Max(0, result.Total - result.Succeeded - result.Failed);
        });

        if (result.Errors?.Count > 0)
        {
            await Dispatcher.Enqueue(() =>
            {
                var existing = new HashSet<string>(Errors.Select(static error => error.UniqueKey));
                foreach (var error in result.Errors)
                {
                    var item = CreateErrorItem(error);
                    if (!existing.Add(item.UniqueKey))
                    {
                        continue;
                    }

                    Errors.Add(item);
                    var detailBuilder = new StringBuilder();
                    if (!string.IsNullOrWhiteSpace(error.FilePath))
                    {
                        detailBuilder.AppendLine($"Soubor: {error.FilePath}");
                    }

                    if (!string.IsNullOrWhiteSpace(error.Suggestion))
                    {
                        detailBuilder.Append(error.Suggestion);
                    }

                    var detail = detailBuilder.Length > 0 ? detailBuilder.ToString() : null;
                    Log.Add(new ImportLogItem(DateTimeOffset.Now, "Chyba", error.Message, "error", detail));
                    TrimLog();
                }

                _clearResultsCommand.NotifyCanExecuteChanged();
            });
        }

        var summary = result.Status switch
        {
            ImportBatchStatus.Success => $"Import dokončen. Úspěšně importováno {result.Succeeded} z {result.Total} souborů.",
            ImportBatchStatus.PartialSuccess => $"Import dokončen s částečným úspěchem ({result.Succeeded}/{result.Total}). Zkontrolujte prosím chyby.",
            ImportBatchStatus.Failure => "Import se nezdařil. Zkontrolujte chyby.",
            ImportBatchStatus.FatalError => "Import byl zastaven kvůli fatální chybě. Opravte problém a zkuste to znovu.",
            _ => "Import dokončen.",
        };

        var logStatus = result.Status switch
        {
            ImportBatchStatus.Success => "success",
            ImportBatchStatus.PartialSuccess => "warning",
            ImportBatchStatus.Failure => "error",
            ImportBatchStatus.FatalError => "error",
            _ => "info",
        };

        await AddLogAsync("Výsledek", summary, logStatus, BuildResultDetail(result)).ConfigureAwait(false);

        switch (result.Status)
        {
            case ImportBatchStatus.Success:
                StatusService.Info(summary);
                break;
            case ImportBatchStatus.PartialSuccess:
            case ImportBatchStatus.Failure:
            case ImportBatchStatus.FatalError:
                StatusService.Error(summary);
                break;
            default:
                StatusService.Info(summary);
                break;
        }

        return true;
    }

    private static string ExtractErrorMessage(ApiResponse<ImportBatchResult> response)
    {
        var error = response.Errors.FirstOrDefault();
        if (error is null)
        {
            return "Import se nezdařil.";
        }

        return string.IsNullOrWhiteSpace(error.Message)
            ? "Import se nezdařil."
            : error.Message;
    }

    private async Task ResetProgressAsync()
    {
        await Dispatcher.Enqueue(() =>
        {
            Processed = 0;
            Total = 0;
            OkCount = 0;
            ErrorCount = 0;
            SkipCount = 0;
            Log.Clear();
            Errors.Clear();
            _clearResultsCommand.NotifyCanExecuteChanged();
        });
    }

    private Task<string> BuildCurrentProgressDetailAsync(object? status = null)
    {
        var statusText = FormatStatusText(status);

        return Dispatcher.EnqueueAsync(() =>
        {
            var skipped = SkipCount > 0 ? SkipCount : Math.Max(0, Total - OkCount - ErrorCount);
            var builder = new StringBuilder();
            builder.AppendLine($"Stav: {statusText}");
            builder.AppendLine($"Celkem souborů: {Total}");
            builder.AppendLine($"Úspěšně: {OkCount}");
            builder.AppendLine($"Chyby: {ErrorCount}");
            builder.Append($"Přeskočeno: {skipped}");

            if (ErrorCount > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.Append("Detaily chyb najdete v seznamu níže.");
            }

            return Task.FromResult(builder.ToString());
        });
    }

    private static string BuildResultDetail(ImportBatchResult result)
    {
        var skipped = Math.Max(0, result.Total - result.Succeeded - result.Failed);
        var statusText = GetStatusText(result.Status);

        var builder = new StringBuilder();
        builder.AppendLine($"Stav: {statusText}");
        builder.AppendLine($"Celkem souborů: {result.Total}");
        builder.AppendLine($"Úspěšně: {result.Succeeded}");
        builder.AppendLine($"Chyby: {result.Failed}");
        builder.Append($"Přeskočeno: {skipped}");

        if (result.Errors?.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append("Detaily chyb najdete v seznamu níže.");
        }

        return builder.ToString();
    }

    private static string FormatStatusText(object? status)
    {
        return status switch
        {
            ImportBatchStatus typed => GetStatusText(typed),
            string text when Enum.TryParse(text, true, out ImportBatchStatus parsed) => GetStatusText(parsed),
            _ => status?.ToString() ?? "Dokončeno",
        };
    }

    private static string GetStatusText(ImportBatchStatus status)
    {
        return status switch
        {
            ImportBatchStatus.Success => "Úspěch",
            ImportBatchStatus.PartialSuccess => "Částečný úspěch",
            ImportBatchStatus.Failure => "Selhání",
            ImportBatchStatus.FatalError => "Fatální chyba",
            _ => status.ToString(),
        };
    }

    private async Task AddLogAsync(string title, string message, string status, string? detail = null)
    {
        await Dispatcher.Enqueue(() =>
        {
            Log.Add(new ImportLogItem(DateTimeOffset.Now, title, message, status, detail));
            TrimLog();
            _clearResultsCommand.NotifyCanExecuteChanged();
        });
    }

    private void TrimLog()
    {
        while (Log.Count > MaxLogEntries)
        {
            Log.RemoveAt(0);
        }
    }

    private void RestoreStateFromHotStorage()
    {
        if (_hotStateService is null)
        {
            return;
        }

        SelectedFolder = _hotStateService.LastFolder;
        Recursive = _hotStateService.ImportRecursive;
        KeepFsMetadata = _hotStateService.ImportKeepFsMetadata;
        SetReadOnly = _hotStateService.ImportSetReadOnly;
        UseParallel = _hotStateService.ImportUseParallel;
        MaxDegreeOfParallelism = _hotStateService.ImportMaxDegreeOfParallelism > 0
            ? _hotStateService.ImportMaxDegreeOfParallelism
            : Environment.ProcessorCount;
        DefaultAuthor = _hotStateService.ImportDefaultAuthor;
        MaxFileSizeMegabytes = _hotStateService.ImportMaxFileSizeMegabytes ?? 0;
    }

    private void PopulateDefaultAuthorFromCurrentUser()
    {
        if (!string.IsNullOrWhiteSpace(DefaultAuthor))
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var userName = Environment.UserName;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return;
        }

        var domainName = Environment.UserDomainName;
        DefaultAuthor = string.IsNullOrWhiteSpace(domainName)
            ? userName
            : $"{domainName}\\{userName}";
    }

    private void OnErrorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasErrors));
        _clearResultsCommand.NotifyCanExecuteChanged();
    }

    private void OnLogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _clearResultsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFolderChanged(string? value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.LastFolder = value;
        }
        _runImportCommand.NotifyCanExecuteChanged();
    }

    partial void OnRecursiveChanged(bool value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.ImportRecursive = value;
        }
    }

    partial void OnKeepFsMetadataChanged(bool value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.ImportKeepFsMetadata = value;
        }
    }

    partial void OnSetReadOnlyChanged(bool value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.ImportSetReadOnly = value;
        }
    }

    partial void OnUseParallelChanged(bool value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.ImportUseParallel = value;
        }
    }

    partial void OnMaxDegreeOfParallelismChanged(int value)
    {
        var normalized = value <= 0 ? Environment.ProcessorCount : value;
        if (value != normalized)
        {
            maxDegreeOfParallelism = normalized;
            OnPropertyChanged(nameof(MaxDegreeOfParallelism));
        }

        if (_hotStateService is not null)
        {
            _hotStateService.ImportMaxDegreeOfParallelism = normalized;
        }
    }

    partial void OnDefaultAuthorChanged(string? value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.ImportDefaultAuthor = value;
        }
    }

    partial void OnMaxFileSizeMegabytesChanged(double value)
    {
        if (double.IsNaN(value) || value < 0)
        {
            maxFileSizeMegabytes = 0;
            OnPropertyChanged(nameof(MaxFileSizeMegabytes));
            value = 0;
        }

        if (_hotStateService is not null)
        {
            _hotStateService.ImportMaxFileSizeMegabytes = value > 0 ? value : null;
        }
    }

    partial void OnIsImportingChanged(bool value)
    {
        _runImportCommand.NotifyCanExecuteChanged();
        _stopImportCommand.NotifyCanExecuteChanged();
        _pickFolderCommand.NotifyCanExecuteChanged();
        _clearResultsCommand.NotifyCanExecuteChanged();
    }

    partial void OnProcessedChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressText));
        _clearResultsCommand.NotifyCanExecuteChanged();
    }

    partial void OnTotalChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressText));
        _clearResultsCommand.NotifyCanExecuteChanged();
    }
}
