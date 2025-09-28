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
        IPickerService? pickerService = null)
        : base(messenger, statusService, dispatcher, exceptionHandler)
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
    }

    private string? _selectedFolder;
    public string? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                OnSelectedFolderChanged(value);
            }
        }
    }

    private bool _recursive = true;
    public bool Recursive
    {
        get => _recursive;
        set
        {
            if (SetProperty(ref _recursive, value))
            {
                OnRecursiveChanged(value);
            }
        }
    }

    private bool _keepFsMetadata = true;
    public bool KeepFsMetadata
    {
        get => _keepFsMetadata;
        set
        {
            if (SetProperty(ref _keepFsMetadata, value))
            {
                OnKeepFsMetadataChanged(value);
            }
        }
    }

    private bool _setReadOnly;
    public bool SetReadOnly
    {
        get => _setReadOnly;
        set
        {
            if (SetProperty(ref _setReadOnly, value))
            {
                OnSetReadOnlyChanged(value);
            }
        }
    }

    private bool _useParallel = true;
    public bool UseParallel
    {
        get => _useParallel;
        set
        {
            if (SetProperty(ref _useParallel, value))
            {
                OnUseParallelChanged(value);
            }
        }
    }

    private int? _maxDegreeOfParallelism = Environment.ProcessorCount;
    public int? MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        set
        {
            if (SetProperty(ref _maxDegreeOfParallelism, value))
            {
                OnMaxDegreeOfParallelismChanged(value);
            }
        }
    }

    private string? _defaultAuthor;
    public string? DefaultAuthor
    {
        get => _defaultAuthor;
        set
        {
            if (SetProperty(ref _defaultAuthor, value))
            {
                OnDefaultAuthorChanged(value);
            }
        }
    }

    private double? _maxFileSizeMegabytes;
    public double? MaxFileSizeMegabytes
    {
        get => _maxFileSizeMegabytes;
        set
        {
            if (SetProperty(ref _maxFileSizeMegabytes, value))
            {
                OnMaxFileSizeMegabytesChanged(value);
            }
        }
    }

    private bool _isImporting;
    public bool IsImporting
    {
        get => _isImporting;
        set
        {
            if (SetProperty(ref _isImporting, value))
            {
                OnIsImportingChanged(value);
            }
        }
    }

    private bool _isIndeterminate;
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set => SetProperty(ref _isIndeterminate, value);
    }

    private int _processed;
    public int Processed
    {
        get => _processed;
        set
        {
            if (SetProperty(ref _processed, value))
            {
                OnProcessedChanged(value);
            }
        }
    }

    private int _total;
    public int Total
    {
        get => _total;
        set
        {
            if (SetProperty(ref _total, value))
            {
                OnTotalChanged(value);
            }
        }
    }

    private long _processedBytes;
    public long ProcessedBytes
    {
        get => _processedBytes;
        set => SetProperty(ref _processedBytes, value);
    }

    private long _totalBytes;
    public long TotalBytes
    {
        get => _totalBytes;
        set => SetProperty(ref _totalBytes, value);
    }

    private double? _progressPercent;
    public double? ProgressPercent
    {
        get => _progressPercent;
        set
        {
            if (SetProperty(ref _progressPercent, value))
            {
                OnProgressPercentChanged(value);
            }
        }
    }

    private string? _currentFileName;
    public string? CurrentFileName
    {
        get => _currentFileName;
        set => SetProperty(ref _currentFileName, value);
    }

    private string? _currentFilePath;
    public string? CurrentFilePath
    {
        get => _currentFilePath;
        set => SetProperty(ref _currentFilePath, value);
    }

    private bool _hasMaxFileSizeError;
    public bool HasMaxFileSizeError
    {
        get => _hasMaxFileSizeError;
        set => SetProperty(ref _hasMaxFileSizeError, value);
    }

    private bool _hasParallelismError;
    public bool HasParallelismError
    {
        get => _hasParallelismError;
        set => SetProperty(ref _hasParallelismError, value);
    }

    private ImportErrorSeverity _selectedErrorFilter = ImportErrorSeverity.All;
    public ImportErrorSeverity SelectedErrorFilter
    {
        get => _selectedErrorFilter;
        set
        {
            if (SetProperty(ref _selectedErrorFilter, value))
            {
                OnSelectedErrorFilterChanged(value);
            }
        }
    }

    public ObservableCollection<ImportLogItem> Log { get; }

    public ObservableCollection<ImportErrorItem> Errors { get; }

    public ReadOnlyObservableCollection<ImportErrorItem> FilteredErrors { get; }

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
            var folder = !string.IsNullOrWhiteSpace(SelectedFolder) && Directory.Exists(SelectedFolder)
                ? SelectedFolder!
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
                throw;
            }

            await AddLogAsync(
                    "Import",
                    $"Spouštím import ze složky '{SelectedFolder}'.",
                    "info",
                    string.IsNullOrWhiteSpace(SelectedFolder) ? null : $"Složka: {SelectedFolder}")
                .ConfigureAwait(false);

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
        return new ImportFolderRequest
        {
            FolderPath = SelectedFolder!.Trim(),
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

        var (cache, totalBytes) = await Task.Run(() =>
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

            return (dictionary, total);
        }, cancellationToken).ConfigureAwait(false);

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
    }

    private async Task HandleBatchCompletedAsync(ImportProgressEvent progress)
    {
        await HandleProgressSnapshotAsync(progress).ConfigureAwait(false);

        var aggregate = progress.Aggregate ?? ImportAggregateResult.EmptySuccess;

        await Dispatcher.Enqueue(() =>
        {
            SkipCount = Math.Max(0, aggregate.Total - aggregate.Succeeded - aggregate.Failed);
            IsIndeterminate = false;
            ProgressPercent = progress.ProgressPercent ?? 100d;
            CurrentFileName = null;
            CurrentFilePath = null;
            ProcessedBytes = TotalBytes;
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
    }

    private void OnLogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _clearResultsCommand.NotifyCanExecuteChanged();
        _exportLogCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectedFolderChanged(string? value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.LastFolder = value;
        }
        _runImportCommand.NotifyCanExecuteChanged();
    }

    private void OnRecursiveChanged(bool value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.ImportRecursive = value;
        }
    }

    private void OnKeepFsMetadataChanged(bool value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.ImportKeepFsMetadata = value;
        }
    }

    private void OnSetReadOnlyChanged(bool value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.ImportSetReadOnly = value;
        }
    }

    private void OnUseParallelChanged(bool value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.ImportUseParallel = value;
        }

        HasParallelismError = value && (!MaxDegreeOfParallelism.HasValue || MaxDegreeOfParallelism.Value <= 0);
        _runImportCommand.NotifyCanExecuteChanged();
    }

    private void OnMaxDegreeOfParallelismChanged(int? value)
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

    private void OnDefaultAuthorChanged(string? value)
    {
        if (_hotStateService is not null)
        {
            _hotStateService.ImportDefaultAuthor = value;
        }
    }

    private void OnMaxFileSizeMegabytesChanged(double? value)
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

    private void OnSelectedErrorFilterChanged(ImportErrorSeverity value)
    {
        UpdateFilteredErrors();
    }

    private void OnIsImportingChanged(bool value)
    {
        _runImportCommand.NotifyCanExecuteChanged();
        _stopImportCommand.NotifyCanExecuteChanged();
        _pickFolderCommand.NotifyCanExecuteChanged();
        _clearResultsCommand.NotifyCanExecuteChanged();
    }

    private void OnProcessedChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressText));
        _clearResultsCommand.NotifyCanExecuteChanged();
    }

    private void OnTotalChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressText));
        _clearResultsCommand.NotifyCanExecuteChanged();
    }

    private void OnProgressPercentChanged(double? value)
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
}
