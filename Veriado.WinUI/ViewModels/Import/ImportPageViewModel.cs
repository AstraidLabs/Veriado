using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
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

        RestoreStateFromHotStorage();

        _pickFolderCommand = new AsyncRelayCommand(ExecutePickFolderAsync, () => !IsImporting);
        _runImportCommand = new AsyncRelayCommand(ExecuteRunImportAsync, CanRunImport);
        _stopImportCommand = new RelayCommand(ExecuteStopImport, () => IsImporting);
        _clearResultsCommand = new RelayCommand(ExecuteClearResults, CanClearResults);
        _openErrorDetailCommand = new RelayCommand<ImportErrorItem>(ExecuteOpenErrorDetail);
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
            await AddLogAsync("Import", $"Spouštím import ze složky '{SelectedFolder}'.", "info").ConfigureAwait(false);

            var request = BuildRequest();

            var handledStreaming = await TryProcessStreamingAsync(request, _importCancellation.Token).ConfigureAwait(false);
            var statusReported = false;

            if (handledStreaming)
            {
                statusReported = true;
                StatusService.Info("Import dokončen.");
            }
            else
            {
                statusReported = await ProcessBatchImportAsync(request, _importCancellation.Token).ConfigureAwait(false);
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
        };
    }

    private async Task<bool> TryProcessStreamingAsync(ImportFolderRequest request, CancellationToken cancellationToken)
    {
        var method = _importService
            .GetType()
            .GetMethod("ImportFolderStreamAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (method is null)
        {
            return false;
        }

        var returnType = method.ReturnType;
        object? invocationResult = method.Invoke(_importService, new object[] { request, cancellationToken });

        if (invocationResult is Task task)
        {
            await task.ConfigureAwait(false);
            invocationResult = GetTaskResult(task);
            returnType = invocationResult?.GetType() ?? returnType;
        }
        else if (invocationResult is not null)
        {
            returnType = invocationResult.GetType();
        }

        if (invocationResult is null)
        {
            return false;
        }

        var asyncEnumerableInterface = returnType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));

        if (asyncEnumerableInterface is null)
        {
            return false;
        }

        var eventType = asyncEnumerableInterface.GetGenericArguments()[0];
        var processMethod = typeof(ImportPageViewModel)
            .GetMethod(nameof(ProcessStreamingEventsAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(eventType);

        var taskResult = (Task<bool>)processMethod.Invoke(this, new object[] { invocationResult, cancellationToken })!;
        return await taskResult.ConfigureAwait(false);
    }

    private async Task<bool> ProcessStreamingEventsAsync<TEvent>(IAsyncEnumerable<TEvent> events, CancellationToken cancellationToken)
        where TEvent : class
    {
        try
        {
            await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (evt is null)
                {
                    continue;
                }

                await HandleStreamingEventAsync(evt).ConfigureAwait(false);
            }

            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private async Task HandleStreamingEventAsync(object evt)
    {
        var typeName = evt.GetType().Name;

        switch (typeName)
        {
            case "ImportStartedEvent":
            case "ImportStarted":
                await AddLogAsync("Import", "Import byl spuštěn.", "info").ConfigureAwait(false);
                break;
            case "ImportTotalKnownEvent":
            case "ImportTotalKnown":
                var totalProperty = evt.GetType().GetProperty("Total") ?? evt.GetType().GetProperty("Count");
                if (totalProperty?.GetValue(evt) is int totalCount)
                {
                    await Dispatcher.Enqueue(() =>
                    {
                        Total = totalCount;
                        IsIndeterminate = false;
                    });
                }

                break;
            case "ImportFileSucceededEvent":
            case "ImportFileOkEvent":
            case "ImportFileOk":
                await HandleFileProgressAsync(evt, status: "success").ConfigureAwait(false);
                break;
            case "ImportFileFailedEvent":
            case "ImportFileFailed":
                await HandleFileProgressAsync(evt, status: "error").ConfigureAwait(false);
                break;
            case "ImportFileSkippedEvent":
            case "ImportFileSkipped":
                await HandleFileProgressAsync(evt, status: "skip").ConfigureAwait(false);
                break;
            case "ImportCompletedEvent":
            case "ImportCompleted":
                await AddLogAsync("Import", "Import byl dokončen.", "success").ConfigureAwait(false);
                break;
            default:
                await HandleUnknownEventAsync(evt).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleFileProgressAsync(object evt, string status)
    {
        var fileProperty = evt.GetType().GetProperty("FilePath") ?? evt.GetType().GetProperty("Path");
        var messageProperty = evt.GetType().GetProperty("Message");
        var errorProperty = evt.GetType().GetProperty("Error");
        var fileName = fileProperty?.GetValue(evt) as string;
        var message = messageProperty?.GetValue(evt) as string ?? errorProperty?.GetValue(evt) as string;

        switch (status)
        {
            case "success":
                await Dispatcher.Enqueue(() =>
                {
                    OkCount++;
                    Processed++;
                });
                await AddLogAsync("OK", fileName ?? "Soubor", status).ConfigureAwait(false);
                break;
            case "error":
                await Dispatcher.Enqueue(() =>
                {
                    ErrorCount++;
                    Processed++;
                    Errors.Add(new ImportErrorItem(fileName ?? "Soubor", message ?? "Import se nezdařil.", null));
                });
                await AddLogAsync("Chyba", message ?? "Import se nezdařil.", status).ConfigureAwait(false);
                break;
            case "skip":
                await Dispatcher.Enqueue(() =>
                {
                    SkipCount++;
                    Processed++;
                });
                await AddLogAsync("Přeskočeno", fileName ?? "Soubor", status).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleUnknownEventAsync(object evt)
    {
        await AddLogAsync("Událost", evt.ToString() ?? evt.GetType().Name, "info").ConfigureAwait(false);
    }

    private static object? GetTaskResult(Task task)
    {
        var type = task.GetType();
        if (!type.IsGenericType)
        {
            return null;
        }

        return type.GetProperty("Result")?.GetValue(task);
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
                foreach (var error in result.Errors)
                {
                    Errors.Add(new ImportErrorItem(
                        Path.GetFileName(error.FilePath),
                        error.Message,
                        null));
                    Log.Add(new ImportLogItem(DateTimeOffset.Now, "Chyba", error.Message, "error"));
                    TrimLog();
                }

                _clearResultsCommand.NotifyCanExecuteChanged();
            });
        }

        var summary = result.Status switch
        {
            ImportBatchStatus.Success => $"Import dokončen. Úspěšně importováno {result.Succeeded} z {result.Total} souborů.",
            ImportBatchStatus.PartialSuccess => $"Import dokončen s částečným úspěchem ({result.Succeeded}/{result.Total}).",
            ImportBatchStatus.Failure => "Import se nezdařil. Zkontrolujte chyby.",
            ImportBatchStatus.FatalError => "Import skončil fatální chybou.",
            _ => "Import dokončen.",
        };

        await AddLogAsync("Výsledek", summary, result.Status == ImportBatchStatus.Success ? "success" : "warning").ConfigureAwait(false);

        if (result.Status == ImportBatchStatus.Success)
        {
            StatusService.Info("Import dokončen.");
        }
        else
        {
            StatusService.Info(summary);
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

    private async Task AddLogAsync(string title, string message, string status)
    {
        await Dispatcher.Enqueue(() =>
        {
            Log.Add(new ImportLogItem(DateTimeOffset.Now, title, message, status));
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
