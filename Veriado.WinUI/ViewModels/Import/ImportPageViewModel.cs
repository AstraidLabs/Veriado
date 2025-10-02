using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Veriado.Contracts.Common;
using Veriado.Contracts.Import;
using Veriado.Services.Import;
using Veriado.WinUI.Models.Import;
using Veriado.WinUI.ViewModels.Base;
using Veriado.WinUI.Views.Import;

namespace Veriado.WinUI.ViewModels.Import;

public partial class ImportPageViewModel : ViewModelBase
{
    private const int MaxLogEntries = 250;

    private readonly IImportService _importService;
    private readonly IHotStateService? _hotStateService;
    private readonly IPickerService? _pickerService;
    private readonly IDialogService _dialogService;
    private readonly AsyncRelayCommand _pickFolderCommand;
    private readonly AsyncRelayCommand _runImportCommand;
    private readonly RelayCommand _stopImportCommand;
    private readonly RelayCommand _clearResultsCommand;
    private readonly AsyncRelayCommand<ImportError> _openErrorDetailCommand;
    private readonly AsyncRelayCommand _exportLogCommand;
    private CancellationTokenSource? _importCancellation;

    private int _okCount;
    private int _errorCount;
    private int _skipCount;
    private readonly Dictionary<string, long> _fileSizeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<ImportErrorItem> _filteredErrors;

    public ImportPageViewModel(
        IImportService importService,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler,
        IDialogService dialogService,
        IHotStateService? hotStateService = null,
        IPickerService? pickerService = null,
        ILocalizationService localizationService)
        : base(messenger, statusService, dispatcher, exceptionHandler, localizationService)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _hotStateService = hotStateService;
        _pickerService = pickerService;

        Log = new ObservableCollection<ImportLogItem>();
        Errors = new ObservableCollection<ImportErrorItem>();
        _filteredErrors = new ObservableCollection<ImportErrorItem>();
        FilteredErrors = new ReadOnlyObservableCollection<ImportErrorItem>(_filteredErrors);
        Log.CollectionChanged += OnLogCollectionChanged;
        Errors.CollectionChanged += OnErrorsCollectionChanged;

        _pickFolderCommand = new AsyncRelayCommand(ExecutePickFolderAsync, () => !IsImporting);
        _runImportCommand = new AsyncRelayCommand(ExecuteRunImportAsync, CanRunImport);
        _stopImportCommand = new RelayCommand(ExecuteStopImport, () => IsImporting);
        _clearResultsCommand = new RelayCommand(ExecuteClearResults, CanClearResults);
        _openErrorDetailCommand = new AsyncRelayCommand<ImportError>(ExecuteOpenErrorDetailAsync);
        _exportLogCommand = new AsyncRelayCommand(ExecuteExportLogAsync, () => Log.Count > 0);

        RestoreStateFromHotStorage();
        PopulateDefaultAuthorFromCurrentUser();
        UpdateFilteredErrors();
        UpdateErrorSummary();
    }

    [ObservableProperty]
    private string? _selectedFolder;

    [ObservableProperty]
    private bool _recursive = true;

    [ObservableProperty]
    private bool _keepFsMetadata = true;

    [ObservableProperty]
    private bool _setReadOnly;

    [ObservableProperty]
    private bool _useParallel = true;

    [ObservableProperty]
    private int? _maxDegreeOfParallelism = Environment.ProcessorCount;

    [ObservableProperty]
    private string? _defaultAuthor;

    [ObservableProperty]
    private double? _maxFileSizeMegabytes;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private int _processed;

    [ObservableProperty]
    private int _total;

    [ObservableProperty]
    private long _processedBytes;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private double? _progressPercent;

    [ObservableProperty]
    private string? _currentFileName;

    [ObservableProperty]
    private string? _currentFilePath;

    [ObservableProperty]
    private bool _hasMaxFileSizeError;

    [ObservableProperty]
    private bool _hasParallelismError;

    [ObservableProperty]
    private bool _isActiveStatusVisible;

    [ObservableProperty]
    private string? _activeStatusTitle;

    [ObservableProperty]
    private string? _activeStatusMessage;

    [ObservableProperty]
    private InfoBarSeverity _activeStatusSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private bool _isDynamicStatusVisible;

    [ObservableProperty]
    private string? _dynamicStatusTitle;

    [ObservableProperty]
    private string? _dynamicStatusMessage;

    [ObservableProperty]
    private InfoBarSeverity _dynamicStatusSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private ImportErrorSeverity _selectedErrorFilter = ImportErrorSeverity.All;

    [ObservableProperty]
    private InfoBarSeverity _errorSummarySeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private string? _errorSummaryTitle;

    [ObservableProperty]
    private string? _errorSummaryMessage;

    [ObservableProperty]
    private string? _errorSummaryDetail;

    public ObservableCollection<ImportLogItem> Log { get; }

    public ObservableCollection<ImportErrorItem> Errors { get; }

    public ReadOnlyObservableCollection<ImportErrorItem> FilteredErrors { get; }

    public IReadOnlyList<ImportErrorSeverity> ErrorFilterOptions { get; } = new[]
    {
        ImportErrorSeverity.All,
        ImportErrorSeverity.Warning,
        ImportErrorSeverity.Error,
        ImportErrorSeverity.Fatal,
    };

    public int OkCount
    {
        get => _okCount;
        private set
        {
            if (SetProperty(ref _okCount, value))
            {
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(PendingCount));
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
                OnPropertyChanged(nameof(PendingCount));
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
                OnPropertyChanged(nameof(PendingCount));
                _clearResultsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ProgressText =>
        $"Zpracováno {Processed}/{(Total > 0 ? Total : 0)} • OK {OkCount} • Chyby {ErrorCount} • Skip {SkipCount} • Čeká {PendingCount}";

    public int PendingCount => CalculatePendingCount(Total, Processed, OkCount, ErrorCount, SkipCount);

    public bool HasErrors => Errors.Count > 0;

    public bool HasFilteredErrors => _filteredErrors.Count > 0;

    public bool HasNoFilteredErrors => !HasFilteredErrors;

    public bool HasErrorSummaryDetail => !string.IsNullOrWhiteSpace(ErrorSummaryDetail);

    public IAsyncRelayCommand PickFolderCommand => _pickFolderCommand;

    public IAsyncRelayCommand RunImportCommand => _runImportCommand;

    public IRelayCommand StopImportCommand => _stopImportCommand;

    public IRelayCommand ClearResultsCommand => _clearResultsCommand;

    public IAsyncRelayCommand<ImportError> OpenErrorDetailCommand => _openErrorDetailCommand;

    public IAsyncRelayCommand ExportLogCommand => _exportLogCommand;

    public double ProgressPercentValue => ProgressPercent ?? 0d;

    public string ProgressPercentDisplay => ProgressPercent.HasValue
        ? $"{ProgressPercent.Value:0.##} %"
        : "0 %";

    private bool CanRunImport() =>
        !IsImporting
        && !string.IsNullOrWhiteSpace(SelectedFolder)
        && !HasParallelismError
        && !HasMaxFileSizeError;

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

        return SafeExecuteAsync(RunImportAsync, "Importuji…");
    }

    private void ExecuteStopImport()
    {
        _importCancellation?.Cancel();
        TryCancelRunning();
        _ = SetActiveStatusAsync("Import zrušen", "Import byl zastaven uživatelem.", InfoBarSeverity.Warning);
        _ = ClearDynamicStatusAsync();
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
        ProcessedBytes = 0;
        TotalBytes = 0;
        ProgressPercent = null;
        CurrentFileName = null;
        CurrentFilePath = null;
        _fileSizeCache.Clear();
        _exportLogCommand.NotifyCanExecuteChanged();
        StatusService.Info("Výsledky importu byly vymazány.");
        _ = ClearActiveStatusAsync();
        _ = ClearDynamicStatusAsync();
    }

    private Task ExecuteOpenErrorDetailAsync(ImportError? error)
    {
        if (error is null)
        {
            return Task.CompletedTask;
        }

        return Dispatcher.EnqueueAsync(async () =>
        {
            var view = new ErrorDetailView
            {
                DataContext = new ErrorDetailViewModel(error),
            };

            await _dialogService.ShowAsync("Detail chyby importu", view, "Zavřít").ConfigureAwait(false);
        });
    }

    private Task ExecuteExportLogAsync()
    {
        if (Log.Count == 0)
        {
            StatusService.Info("Protokol je prázdný, není co exportovat.");
            return Task.CompletedTask;
        }

        return SafeExecuteAsync(async token =>
        {
            var selectedFolder = SelectedFolder;
            var folder = !string.IsNullOrWhiteSpace(selectedFolder) && Directory.Exists(selectedFolder)
                ? selectedFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                folder = Path.GetTempPath();
            }

            var timestamp = DateTimeOffset.Now.ToString("yyyyMMddHHmmss");
            var fileName = $"import-log-{timestamp}.txt";
            var targetPath = Path.Combine(folder, fileName);

            var builder = new StringBuilder();
            foreach (var item in Log)
            {
                builder.AppendLine($"[{item.FormattedTimestamp}] {item.Title}: {item.Message}");
                if (!string.IsNullOrWhiteSpace(item.Detail))
                {
                    builder.AppendLine(item.Detail);
                }

                builder.AppendLine();
            }

            await File.WriteAllTextAsync(targetPath, builder.ToString(), Encoding.UTF8, token).ConfigureAwait(false);
            StatusService.Info($"Protokol byl exportován do souboru '{targetPath}'.");
        }, "Exportuji protokol…");
    }

    private async Task RunImportAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SelectedFolder))
        {
            StatusService.Error("Vyberte složku pro import.");
            return;
        }

        await ClearDynamicStatusAsync().ConfigureAwait(false);
        var initialFolder = SelectedFolder;
        var preparationMessage = string.IsNullOrWhiteSpace(initialFolder)
            ? "Připravuji import…"
            : $"Připravuji import ze složky '{initialFolder}'.";
        await SetActiveStatusAsync("Příprava importu", preparationMessage, InfoBarSeverity.Informational).ConfigureAwait(false);
        await SetDynamicStatusAsync("Příprava importu", "Analyzuji vybranou složku…", InfoBarSeverity.Informational).ConfigureAwait(false);

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

            var request = BuildRequest();
            var options = BuildImportOptions();
            var folderPath = request.FolderPath;

            try
            {
                await InitializeFileSizeCacheAsync(folderPath, _importCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await AddLogAsync("Import", "Příprava byla zrušena.", "warning").ConfigureAwait(false);
                await SetActiveStatusAsync("Import zrušen", "Příprava byla zrušena.", InfoBarSeverity.Warning).ConfigureAwait(false);
                await SetDynamicStatusAsync("Import zrušen", "Import byl zrušen před spuštěním.", InfoBarSeverity.Warning).ConfigureAwait(false);
                throw;
            }

            await SetDynamicStatusAsync("Příprava dokončena", "Příprava importu dokončena. Spouštím zpracování…", InfoBarSeverity.Informational)
                .ConfigureAwait(false);
            await AddLogAsync(
                    "Import",
                    $"Spouštím import ze složky '{SelectedFolder}'.",
                    "info",
                    string.IsNullOrWhiteSpace(SelectedFolder) ? null : $"Složka: {SelectedFolder}")
                .ConfigureAwait(false);
            await SetActiveStatusAsync("Import běží", $"Spouštím import ze složky '{folderPath}'.", InfoBarSeverity.Informational).ConfigureAwait(false);
            await SetDynamicStatusAsync("Import běží", "Import byl spuštěn.", InfoBarSeverity.Informational).ConfigureAwait(false);

            var statusReported = false;

            try
            {
                await foreach (var progress in _importService.ImportFolderStreamAsync(folderPath, options, _importCancellation.Token).ConfigureAwait(false))
                {
                    await HandleProgressEventAsync(progress).ConfigureAwait(false);
                    if (progress.Kind == ImportProgressKind.BatchCompleted)
                    {
                        statusReported = true;
                    }
                }
            }
            catch (NotSupportedException)
            {
                statusReported = await ProcessBatchImportAsync(request, _importCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await AddLogAsync("Import", "Import byl zrušen uživatelem.", "warning").ConfigureAwait(false);
                StatusService.Info("Import byl zrušen.");
                await SetActiveStatusAsync("Import zrušen", "Import byl zrušen uživatelem.", InfoBarSeverity.Warning).ConfigureAwait(false);
                await ClearDynamicStatusAsync().ConfigureAwait(false);
                return;
            }

            await Dispatcher.Enqueue(() => IsIndeterminate = false);

            if (!statusReported)
            {
                StatusService.Info("Import dokončen.");
                await SetActiveStatusAsync("Import dokončen", "Import dokončen.", InfoBarSeverity.Informational).ConfigureAwait(false);
                await ClearDynamicStatusAsync().ConfigureAwait(false);
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
        var selectedFolder = SelectedFolder?.Trim();

        return new ImportFolderRequest
        {
            FolderPath = selectedFolder ?? string.Empty,
            Recursive = Recursive,
            KeepFsMetadata = KeepFsMetadata,
            SetReadOnly = SetReadOnly,
            MaxDegreeOfParallelism = ResolveParallelism(),
            DefaultAuthor = string.IsNullOrWhiteSpace(DefaultAuthor) ? null : DefaultAuthor,
            MaxFileSizeBytes = CalculateMaxFileSizeBytes(),
        };
    }

    private ImportOptions BuildImportOptions()
    {
        return new ImportOptions
        {
            MaxFileSizeBytes = CalculateMaxFileSizeBytes(),
            MaxDegreeOfParallelism = ResolveParallelism(),
            KeepFileSystemMetadata = KeepFsMetadata,
            SetReadOnly = SetReadOnly,
            Recursive = Recursive,
            DefaultAuthor = string.IsNullOrWhiteSpace(DefaultAuthor) ? null : DefaultAuthor,
            SearchPattern = "*",
        };
    }

    private int ResolveParallelism()
    {
        if (!UseParallel)
        {
            return 1;
        }

        if (!MaxDegreeOfParallelism.HasValue || MaxDegreeOfParallelism.Value <= 0)
        {
            return Environment.ProcessorCount;
        }

        return MaxDegreeOfParallelism.Value;
    }

    private long? CalculateMaxFileSizeBytes()
    {
        if (!MaxFileSizeMegabytes.HasValue || MaxFileSizeMegabytes.Value <= 0)
        {
            return null;
        }

        var bytes = MaxFileSizeMegabytes.Value * 1024d * 1024d;
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

    private async Task InitializeFileSizeCacheAsync(string folderPath, CancellationToken cancellationToken)
    {
        var recursive = Recursive;

        var result = await Task.Run(() =>
        {
            var dictionary = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            try
            {
                foreach (var file in Directory.EnumerateFiles(folderPath, "*", option))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        dictionary[file] = info.Length;
                        total += info.Length;
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
            }

            return (Cache: dictionary, TotalBytes: total);
        }, cancellationToken).ConfigureAwait(false);

        var cache = result.Cache;
        var totalBytes = result.TotalBytes;

        await Dispatcher.Enqueue(() =>
        {
            _fileSizeCache.Clear();
            foreach (var kvp in cache)
            {
                _fileSizeCache[kvp.Key] = kvp.Value;
            }

            TotalBytes = totalBytes;
            ProcessedBytes = 0;
        });
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
            ProcessedBytes = 0;
            ProgressPercent = progress.ProgressPercent;
            CurrentFileName = null;
            CurrentFilePath = null;
            IsIndeterminate = !progress.TotalFiles.HasValue || progress.TotalFiles.Value <= 0;
        });

        var message = string.IsNullOrWhiteSpace(progress.Message)
            ? "Import byl spuštěn."
            : progress.Message;
        await AddLogAsync("Import", message, "info").ConfigureAwait(false);
        await SetActiveStatusAsync("Import běží", message, InfoBarSeverity.Informational).ConfigureAwait(false);
        var dynamicMessage = BuildDynamicStatusMessage(progress);
        await SetDynamicStatusAsync("Průběh importu", dynamicMessage, InfoBarSeverity.Informational).ConfigureAwait(false);
    }

    private async Task HandleProgressSnapshotAsync(ImportProgressEvent progress)
    {
        await Dispatcher.Enqueue(() =>
        {
            var currentTotal = progress.TotalFiles ?? Total;
            var currentProcessed = progress.ProcessedFiles ?? Processed;
            var currentSucceeded = progress.SucceededFiles ?? OkCount;
            var currentFailed = progress.FailedFiles ?? ErrorCount;
            var currentSkipped = progress.SkippedFiles ?? SkipCount;

            Total = currentTotal;
            Processed = currentProcessed;
            OkCount = currentSucceeded;
            ErrorCount = currentFailed;
            SkipCount = progress.SkippedFiles.HasValue
                ? Math.Max(0, currentSkipped)
                : Math.Max(0, currentTotal - currentSucceeded - currentFailed);

            if (progress.TotalFiles.HasValue)
            {
                IsIndeterminate = currentTotal <= 0;
            }

            if (progress.ProgressPercent.HasValue)
            {
                ProgressPercent = progress.ProgressPercent;
            }

            if (!string.IsNullOrWhiteSpace(progress.FilePath))
            {
                CurrentFileName = Path.GetFileName(progress.FilePath);
                CurrentFilePath = progress.FilePath;
            }
        });

        var message = BuildDynamicStatusMessage(progress);
        await SetDynamicStatusAsync("Průběh importu", message, InfoBarSeverity.Informational).ConfigureAwait(false);
    }

    private async Task HandleFileCompletedAsync(ImportProgressEvent progress)
    {
        var filePath = progress.FilePath;
        var size = !string.IsNullOrWhiteSpace(filePath) ? ResolveFileSize(filePath!) : 0;

        await Dispatcher.Enqueue(() =>
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                CurrentFileName = Path.GetFileName(filePath);
                CurrentFilePath = filePath;

                if (size > 0)
                {
                    var updated = ProcessedBytes + size;
                    ProcessedBytes = TotalBytes > 0 ? Math.Min(updated, TotalBytes) : updated;
                }
            }

            if (progress.ProgressPercent.HasValue)
            {
                ProgressPercent = progress.ProgressPercent;
            }
        });

        var fileName = string.IsNullOrWhiteSpace(filePath)
            ? "Soubor"
            : Path.GetFileName(filePath);
        var detail = string.IsNullOrWhiteSpace(filePath) ? null : filePath;

        await AddLogAsync("OK", fileName, "success", detail).ConfigureAwait(false);
        var completionMessage = string.IsNullOrWhiteSpace(fileName)
            ? "Soubor byl úspěšně importován."
            : $"Soubor {fileName} byl úspěšně importován.";
        await SetDynamicStatusAsync("Soubor dokončen", completionMessage, InfoBarSeverity.Success).ConfigureAwait(false);
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

            if (!string.IsNullOrWhiteSpace(progress.FilePath))
            {
                CurrentFileName = Path.GetFileName(progress.FilePath);
                CurrentFilePath = progress.FilePath;
            }

            if (progress.ProgressPercent.HasValue)
            {
                ProgressPercent = progress.ProgressPercent;
            }
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
        var errorMessage = string.IsNullOrWhiteSpace(progress.Error.Message)
            ? "Došlo k chybě při importu."
            : progress.Error.Message;
        await SetDynamicStatusAsync("Chyba importu", errorMessage, InfoBarSeverity.Error).ConfigureAwait(false);
    }

    private async Task HandleBatchCompletedAsync(ImportProgressEvent progress)
    {
        await HandleProgressSnapshotAsync(progress).ConfigureAwait(false);

        var aggregate = progress.Aggregate ?? ImportAggregateResult.EmptySuccess;

        await Dispatcher.Enqueue(() =>
        {
            SkipCount = Math.Max(0, aggregate.Skipped);
            IsIndeterminate = false;
            ProgressPercent = progress.ProgressPercent ?? 100d;
            CurrentFileName = null;
            CurrentFilePath = null;
            Processed = aggregate.Processed;
            Total = aggregate.Total;
            OkCount = aggregate.Succeeded;
            ErrorCount = aggregate.Failed;
            ProcessedBytes = TotalBytes;
        });

        await EnsureAggregateErrorsAsync(aggregate).ConfigureAwait(false);

        var result = new ImportBatchResult(
            aggregate.Status,
            aggregate.Total,
            aggregate.Processed,
            aggregate.Succeeded,
            aggregate.Failed,
            aggregate.Skipped,
            aggregate.Errors);

        var skipped = Math.Max(0, aggregate.Skipped);
        var pending = CalculatePendingCount(aggregate.Total, aggregate.Processed, aggregate.Succeeded, aggregate.Failed, skipped);
        var skippedSummary = skipped > 0
            ? $" (přeskočeno {skipped})"
            : string.Empty;
        var pendingSummary = pending > 0
            ? $" (čeká {pending})"
            : string.Empty;
        var pendingSentence = pending > 0
            ? $" Zbývá zpracovat {pending} souborů."
            : string.Empty;

        var summary = aggregate.Status switch
        {
            ImportBatchStatus.Success => $"Import dokončen. Úspěšně importováno {aggregate.Succeeded} z {aggregate.Total} souborů{skippedSummary}{pendingSummary}.",
            ImportBatchStatus.PartialSuccess => $"Import dokončen s částečným úspěchem ({aggregate.Succeeded}/{aggregate.Total}{skippedSummary}{pendingSummary}). Zkontrolujte prosím chyby.",
            ImportBatchStatus.Failure => $"Import se nezdařil.{pendingSentence} Zkontrolujte chyby.",
            ImportBatchStatus.FatalError => $"Import byl zastaven kvůli fatální chybě.{pendingSentence} Opravte problém a zkuste to znovu.",
            _ => $"Import dokončen.{pendingSentence}",
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

        var severity = MapStatusToSeverity(aggregate.Status);
        await SetActiveStatusAsync("Stav importu", summary, severity).ConfigureAwait(false);
        await SetDynamicStatusAsync("Shrnutí importu", summary, severity).ConfigureAwait(false);
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
        return new ImportErrorItem(error);
    }

    private async Task<bool> ProcessBatchImportAsync(ImportFolderRequest request, CancellationToken cancellationToken)
    {
        var response = await _importService.ImportFolderAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Data is null)
        {
            var message = ExtractErrorMessage(response);
            await AddLogAsync("Import selhal", message, "error").ConfigureAwait(false);
            StatusService.Error(message);
            await SetActiveStatusAsync("Import selhal", message, InfoBarSeverity.Error).ConfigureAwait(false);
            await SetDynamicStatusAsync("Import selhal", message, InfoBarSeverity.Error).ConfigureAwait(false);
            return true;
        }

        var result = response.Data;

        await Dispatcher.Enqueue(() =>
        {
            Total = result.Total;
            Processed = result.Processed;
            OkCount = result.Succeeded;
            ErrorCount = result.Failed;
            SkipCount = Math.Max(0, result.Skipped);
            ProgressPercent = 100d;
            CurrentFileName = null;
            CurrentFilePath = null;
            ProcessedBytes = TotalBytes;
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
                    var statusText = GetLogStatusDisplay("error");
                    Log.Add(new ImportLogItem(DateTimeOffset.Now, "Chyba", error.Message, statusText, detail));
                    TrimLog();
                }

                _clearResultsCommand.NotifyCanExecuteChanged();
            });
        }

        var skipped = Math.Max(0, result.Skipped);
        var pending = CalculatePendingCount(result.Total, result.Processed, result.Succeeded, result.Failed, skipped);
        var skippedSummary = skipped > 0
            ? $" (přeskočeno {skipped})"
            : string.Empty;
        var pendingSummary = pending > 0
            ? $" (čeká {pending})"
            : string.Empty;
        var pendingSentence = pending > 0
            ? $" Zbývá zpracovat {pending} souborů."
            : string.Empty;

        var summary = result.Status switch
        {
            ImportBatchStatus.Success => $"Import dokončen. Úspěšně importováno {result.Succeeded} z {result.Total} souborů{skippedSummary}{pendingSummary}.",
            ImportBatchStatus.PartialSuccess => $"Import dokončen s částečným úspěchem ({result.Succeeded}/{result.Total}{skippedSummary}{pendingSummary}). Zkontrolujte prosím chyby.",
            ImportBatchStatus.Failure => $"Import se nezdařil.{pendingSentence} Zkontrolujte chyby.",
            ImportBatchStatus.FatalError => $"Import byl zastaven kvůli fatální chybě.{pendingSentence} Opravte problém a zkuste to znovu.",
            _ => $"Import dokončen.{pendingSentence}",
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

        var severity = MapStatusToSeverity(result.Status);
        await SetActiveStatusAsync("Stav importu", summary, severity).ConfigureAwait(false);
        await SetDynamicStatusAsync("Shrnutí importu", summary, severity).ConfigureAwait(false);

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
        _fileSizeCache.Clear();
        await Dispatcher.Enqueue(() =>
        {
            Processed = 0;
            Total = 0;
            OkCount = 0;
            ErrorCount = 0;
            SkipCount = 0;
            ProcessedBytes = 0;
            TotalBytes = 0;
            ProgressPercent = null;
            CurrentFileName = null;
            CurrentFilePath = null;
            Log.Clear();
            Errors.Clear();
            _clearResultsCommand.NotifyCanExecuteChanged();
            _exportLogCommand.NotifyCanExecuteChanged();
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
            builder.AppendLine($"Zpracováno: {Processed}");
            builder.AppendLine($"Úspěšně: {OkCount}");
            builder.AppendLine($"Chyby: {ErrorCount}");
            builder.AppendLine($"Přeskočeno: {skipped}");
            builder.Append($"Čeká: {PendingCount}");

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
        var skipped = Math.Max(0, result.Skipped);
        var statusText = GetStatusText(result.Status);

        var builder = new StringBuilder();
        builder.AppendLine($"Stav: {statusText}");
        builder.AppendLine($"Celkem souborů: {result.Total}");
        builder.AppendLine($"Zpracováno: {result.Processed}");
        builder.AppendLine($"Úspěšně: {result.Succeeded}");
        builder.AppendLine($"Chyby: {result.Failed}");
        builder.AppendLine($"Přeskočeno: {skipped}");
        var pending = CalculatePendingCount(result.Total, result.Processed, result.Succeeded, result.Failed, skipped);
        builder.Append($"Čeká: {pending}");

        if (result.Errors?.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append("Detaily chyb najdete v seznamu níže.");
        }

        return builder.ToString();
    }

    private static int CalculatePendingCount(int total, int processed, int succeeded, int failed, int skipped)
    {
        var normalizedProcessed = Math.Max(processed, succeeded + failed + skipped);
        return Math.Max(0, total - normalizedProcessed);
    }

    private static string NormalizeLogStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "info";
        }

        var trimmed = status.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "success" or "ok" or "completed" => "success",
            "warning" or "warn" or "caution" => "warning",
            "error" or "fail" or "failure" or "fatal" => "error",
            "info" or "information" or "status" => "info",
            _ => trimmed.ToLowerInvariant(),
        };
    }

    private static string GetLogStatusDisplay(string normalizedStatus)
    {
        return normalizedStatus switch
        {
            "success" => "Úspěch",
            "warning" => "Varování",
            "error" => "Chyba",
            "info" => "Informace",
            _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalizedStatus),
        };
    }

    private async Task SetActiveStatusAsync(string title, string? message, InfoBarSeverity severity)
    {
        await Dispatcher.Enqueue(() =>
        {
            ActiveStatusTitle = title;
            ActiveStatusMessage = message;
            ActiveStatusSeverity = severity;
            IsActiveStatusVisible = true;
        });
    }

    private async Task ClearActiveStatusAsync()
    {
        await Dispatcher.Enqueue(() =>
        {
            ActiveStatusTitle = null;
            ActiveStatusMessage = null;
            ActiveStatusSeverity = InfoBarSeverity.Informational;
            IsActiveStatusVisible = false;
        });
    }

    private async Task SetDynamicStatusAsync(string title, string? message, InfoBarSeverity severity)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(message))
        {
            await ClearDynamicStatusAsync();
            return;
        }

        await Dispatcher.Enqueue(() =>
        {
            DynamicStatusTitle = title;
            DynamicStatusMessage = message;
            DynamicStatusSeverity = severity;
            IsDynamicStatusVisible = true;
        });
    }

    private async Task ClearDynamicStatusAsync()
    {
        await Dispatcher.Enqueue(() =>
        {
            DynamicStatusTitle = null;
            DynamicStatusMessage = null;
            DynamicStatusSeverity = InfoBarSeverity.Informational;
            IsDynamicStatusVisible = false;
        });
    }

    private static string BuildDynamicStatusMessage(ImportProgressEvent progress)
    {
        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            return progress.Message;
        }

        if (!string.IsNullOrWhiteSpace(progress.FilePath))
        {
            var fileName = Path.GetFileName(progress.FilePath);
            return string.IsNullOrWhiteSpace(fileName)
                ? "Zpracovávám soubor…"
                : $"Zpracovávám soubor {fileName}";
        }

        if (progress.ProcessedFiles.HasValue && progress.TotalFiles.HasValue)
        {
            var total = progress.TotalFiles.Value;
            var processed = progress.ProcessedFiles.Value;
            var succeeded = progress.SucceededFiles ?? processed;
            var failed = progress.FailedFiles ?? 0;
            var skipped = progress.SkippedFiles ?? 0;
            var pending = CalculatePendingCount(total, processed, succeeded, failed, skipped);
            var baseMessage = $"Zpracováno {processed}/{total} souborů.";
            return pending > 0
                ? $"{baseMessage} Čeká {pending}."
                : baseMessage;
        }

        if (progress.ProcessedFiles.HasValue)
        {
            return $"Zpracováno {progress.ProcessedFiles} souborů.";
        }

        return "Import probíhá…";
    }

    private static InfoBarSeverity MapStatusToSeverity(ImportBatchStatus status)
    {
        return status switch
        {
            ImportBatchStatus.Success => InfoBarSeverity.Success,
            ImportBatchStatus.PartialSuccess => InfoBarSeverity.Warning,
            ImportBatchStatus.Failure => InfoBarSeverity.Error,
            ImportBatchStatus.FatalError => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        };
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
        var normalizedStatus = NormalizeLogStatus(status);
        var displayStatus = GetLogStatusDisplay(normalizedStatus);
        await Dispatcher.Enqueue(() =>
        {
            Log.Add(new ImportLogItem(DateTimeOffset.Now, title, message, displayStatus, detail));
            TrimLog();
            _clearResultsCommand.NotifyCanExecuteChanged();
            _exportLogCommand.NotifyCanExecuteChanged();
        });
    }

    private void TrimLog()
    {
        while (Log.Count > MaxLogEntries)
        {
            Log.RemoveAt(0);
        }
    }

    private long ResolveFileSize(string filePath)
    {
        if (_fileSizeCache.TryGetValue(filePath, out var size))
        {
            return size;
        }

        try
        {
            var info = new FileInfo(filePath);
            if (info.Exists)
            {
                size = info.Length;
                _fileSizeCache[filePath] = size;
                return size;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return 0;
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
        UpdateFilteredErrors();
        UpdateErrorSummary();
    }

    private void OnLogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _clearResultsCommand.NotifyCanExecuteChanged();
        _exportLogCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFolderChanged(string? value)
    {
        var normalized = NormalizeFolderPath(value);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.Equals(normalized, value, comparison))
        {
            _selectedFolder = normalized;
            OnPropertyChanged(nameof(SelectedFolder));
            value = normalized;
        }

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

        HasParallelismError = value && (!MaxDegreeOfParallelism.HasValue || MaxDegreeOfParallelism.Value <= 0);
        _runImportCommand.NotifyCanExecuteChanged();
    }

    partial void OnMaxDegreeOfParallelismChanged(int? value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.ImportMaxDegreeOfParallelism = value.HasValue && value.Value > 0
                ? value.Value
                : Environment.ProcessorCount;
        }

        HasParallelismError = UseParallel && (!value.HasValue || value.Value <= 0);
        _runImportCommand.NotifyCanExecuteChanged();
    }

    partial void OnDefaultAuthorChanged(string? value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.ImportDefaultAuthor = value;
        }
    }

    partial void OnMaxFileSizeMegabytesChanged(double? value)
    {
        var hasError = false;
        double? sanitized = value;

        if (value.HasValue)
        {
            if (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0)
            {
                sanitized = null;
                hasError = true;
            }
        }

        if (!Nullable.Equals(sanitized, value))
        {
            _maxFileSizeMegabytes = sanitized;
            OnPropertyChanged(nameof(MaxFileSizeMegabytes));
        }

        if (_hotStateService is not null)
        {
            _hotStateService.ImportMaxFileSizeMegabytes = sanitized.HasValue && sanitized.Value > 0 ? sanitized : null;
        }

        HasMaxFileSizeError = hasError;
        _runImportCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedErrorFilterChanged(ImportErrorSeverity value)
    {
        UpdateFilteredErrors();
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
        OnPropertyChanged(nameof(PendingCount));
        _clearResultsCommand.NotifyCanExecuteChanged();
    }

    partial void OnTotalChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(PendingCount));
        _clearResultsCommand.NotifyCanExecuteChanged();
    }

    partial void OnProgressPercentChanged(double? value)
    {
        OnPropertyChanged(nameof(ProgressPercentValue));
        OnPropertyChanged(nameof(ProgressPercentDisplay));
    }

    private void UpdateFilteredErrors()
    {
        var items = Errors.Where(MatchesSelectedFilter).ToList();

        if (_filteredErrors.Count == items.Count && _filteredErrors.SequenceEqual(items))
        {
            return;
        }

        _filteredErrors.Clear();

        foreach (var item in items)
        {
            _filteredErrors.Add(item);
        }

        OnPropertyChanged(nameof(HasFilteredErrors));
        OnPropertyChanged(nameof(HasNoFilteredErrors));
    }

    private void UpdateErrorSummary()
    {
        if (Errors.Count == 0)
        {
            ErrorSummarySeverity = InfoBarSeverity.Informational;
            ErrorSummaryTitle = null;
            ErrorSummaryMessage = null;
            ErrorSummaryDetail = null;
            OnPropertyChanged(nameof(HasErrorSummaryDetail));
            return;
        }

        var fatalCount = Errors.Count(static item => item.Severity == ImportErrorSeverity.Fatal);
        var errorCount = Errors.Count(static item => item.Severity == ImportErrorSeverity.Error);
        var warningCount = Errors.Count(static item => item.Severity == ImportErrorSeverity.Warning);

        ErrorSummarySeverity = fatalCount > 0 || errorCount > 0
            ? InfoBarSeverity.Error
            : InfoBarSeverity.Warning;

        ErrorSummaryTitle = fatalCount > 0
            ? "Import obsahuje fatální chyby"
            : errorCount > 0
                ? "Import obsahuje chyby"
                : "Import dokončen s varováními";

        var summaryParts = new List<string>();

        if (fatalCount > 0)
        {
            summaryParts.Add($"{fatalCount}× fatální chyba");
        }

        if (errorCount > 0)
        {
            summaryParts.Add($"{errorCount}× chyba");
        }

        if (warningCount > 0)
        {
            summaryParts.Add($"{warningCount}× varování");
        }

        ErrorSummaryMessage = summaryParts.Count > 0
            ? $"Celkem {Errors.Count} problémů: {string.Join(", ", summaryParts)}."
            : $"Celkem {Errors.Count} problémů.";

        ErrorSummaryDetail = "Vyberte řádek v tabulce, chcete-li zobrazit doporučení a další detaily. Chyby můžete filtrovat podle závažnosti.";
        OnPropertyChanged(nameof(HasErrorSummaryDetail));
    }

    private bool MatchesSelectedFilter(ImportErrorItem item)
    {
        return SelectedErrorFilter switch
        {
            ImportErrorSeverity.Warning => item.Severity == ImportErrorSeverity.Warning,
            ImportErrorSeverity.Error => item.Severity == ImportErrorSeverity.Error,
            ImportErrorSeverity.Fatal => item.Severity == ImportErrorSeverity.Fatal,
            _ => true,
        };
    }

    private static string? NormalizeFolderPath(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        var trimmed = folder.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            trimmed = trimmed[1..^1];
        }

        try
        {
            trimmed = Path.GetFullPath(trimmed);
        }
        catch (Exception)
        {
            return trimmed;
        }

        return Path.TrimEndingDirectorySeparator(trimmed);
    }
}
