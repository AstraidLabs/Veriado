using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Veriado.Contracts.Files;
using Veriado.Infrastructure.Lifecycle;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed partial class HotStateService : ObservableObject, IHotStateService
{
    private static readonly TimeSpan PersistDebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly ISettingsService _settingsService;
    private readonly IStatusService _statusService;
    private readonly IAppLifecycleService _lifecycleService;
    private readonly ILogger<HotStateService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _persistSync = new();
    private bool _initialized;
    private ValidityThresholds _validityThresholds = AppSettings.CreateDefaultValidityThresholds();
    private CancellationTokenSource? _persistSource;
    private Task? _persistTask;

    [ObservableProperty]
    private string? lastQuery;

    [ObservableProperty]
    private string? lastFolder;

    [ObservableProperty]
    private int pageSize = AppSettings.DefaultPageSize;

    [ObservableProperty]
    private bool importRecursive = true;

    [ObservableProperty]
    private bool importKeepFsMetadata = true;

    [ObservableProperty]
    private bool importSetReadOnly;

    [ObservableProperty]
    private bool importUseParallel = true;

    [ObservableProperty]
    private int importMaxDegreeOfParallelism = Environment.ProcessorCount;

    [ObservableProperty]
    private string? importDefaultAuthor;

    [ObservableProperty]
    private double? importMaxFileSizeMegabytes;

    [ObservableProperty]
    private bool importAutoExportLog;

    public ValidityThresholds ValidityThresholds => _validityThresholds;

    public int ValidityRedThresholdDays
    {
        get => _validityThresholds.RedDays;
        set => UpdateValidityThresholds(red: value);
    }

    public int ValidityOrangeThresholdDays
    {
        get => _validityThresholds.OrangeDays;
        set => UpdateValidityThresholds(orange: value);
    }

    public int ValidityGreenThresholdDays
    {
        get => _validityThresholds.GreenDays;
        set => UpdateValidityThresholds(green: value);
    }

    public HotStateService(
        ISettingsService settingsService,
        IStatusService statusService,
        IAppLifecycleService lifecycleService,
        ILogger<HotStateService> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _lifecycleService = lifecycleService ?? throw new ArgumentNullException(nameof(lifecycleService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifecycleService.RunToken);
        var effectiveToken = linkedCts.Token;

        await _gate.WaitAsync(effectiveToken).ConfigureAwait(false);
        try
        {
            var settings = await _settingsService.GetAsync(effectiveToken).ConfigureAwait(false);
            lastQuery = settings.LastQuery;
            lastFolder = settings.LastFolder;
            pageSize = settings.PageSize > 0 ? settings.PageSize : AppSettings.DefaultPageSize;

            var import = settings.Import ?? new ImportPreferences();
            importRecursive = import.Recursive ?? true;
            importKeepFsMetadata = import.KeepFsMetadata ?? true;
            importSetReadOnly = import.SetReadOnly ?? false;
            importUseParallel = import.UseParallel ?? true;
            importMaxDegreeOfParallelism = import.MaxDegreeOfParallelism.HasValue && import.MaxDegreeOfParallelism.Value > 0
                ? import.MaxDegreeOfParallelism.Value
                : Environment.ProcessorCount;
            importDefaultAuthor = import.DefaultAuthor;
            importMaxFileSizeMegabytes = import.MaxFileSizeMegabytes.HasValue && import.MaxFileSizeMegabytes.Value > 0
                ? import.MaxFileSizeMegabytes
                : null;
            importAutoExportLog = import.AutoExportLog ?? false;

            var validity = settings.Validity ?? new ValidityPreferences();
            var defaults = AppSettings.CreateDefaultValidityThresholds();
            var normalizedThresholds = ValidityThresholds.Normalize(
                validity.RedThresholdDays ?? defaults.RedDays,
                validity.OrangeThresholdDays ?? defaults.OrangeDays,
                validity.GreenThresholdDays ?? defaults.GreenDays);
            SetValidityThresholds(normalizedThresholds, persist: false);

            OnPropertyChanged(nameof(ImportRecursive));
            OnPropertyChanged(nameof(ImportKeepFsMetadata));
            OnPropertyChanged(nameof(ImportSetReadOnly));
            OnPropertyChanged(nameof(ImportUseParallel));
            OnPropertyChanged(nameof(ImportMaxDegreeOfParallelism));
            OnPropertyChanged(nameof(ImportDefaultAuthor));
            OnPropertyChanged(nameof(ImportMaxFileSizeMegabytes));
            OnPropertyChanged(nameof(ImportAutoExportLog));
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    partial void OnLastQueryChanged(string? value) => SchedulePersist();

    partial void OnLastFolderChanged(string? value) => SchedulePersist();

    partial void OnPageSizeChanged(int value)
    {
        if (value <= 0)
        {
            pageSize = AppSettings.DefaultPageSize;
            OnPropertyChanged(nameof(PageSize));
        }

        SchedulePersist();
    }

    partial void OnImportRecursiveChanged(bool value) => SchedulePersist();

    partial void OnImportKeepFsMetadataChanged(bool value) => SchedulePersist();

    partial void OnImportSetReadOnlyChanged(bool value) => SchedulePersist();

    partial void OnImportUseParallelChanged(bool value) => SchedulePersist();

    partial void OnImportAutoExportLogChanged(bool value) => SchedulePersist();

    partial void OnImportMaxDegreeOfParallelismChanged(int value)
    {
        if (value <= 0)
        {
            importMaxDegreeOfParallelism = Environment.ProcessorCount;
            OnPropertyChanged(nameof(ImportMaxDegreeOfParallelism));
        }

        SchedulePersist();
    }

    partial void OnImportDefaultAuthorChanged(string? value) => SchedulePersist();

    partial void OnImportMaxFileSizeMegabytesChanged(double? value)
    {
        if (value.HasValue && value.Value <= 0)
        {
            importMaxFileSizeMegabytes = null;
            OnPropertyChanged(nameof(ImportMaxFileSizeMegabytes));
        }

        SchedulePersist();
    }

    private void UpdateValidityThresholds(int? red = null, int? orange = null, int? green = null)
    {
        var normalized = ValidityThresholds.Normalize(
            red ?? _validityThresholds.RedDays,
            orange ?? _validityThresholds.OrangeDays,
            green ?? _validityThresholds.GreenDays);

        SetValidityThresholds(normalized, persist: true);
    }

    private void SetValidityThresholds(ValidityThresholds thresholds, bool persist)
    {
        if (_validityThresholds == thresholds)
        {
            return;
        }

        _validityThresholds = thresholds;
        OnPropertyChanged(nameof(ValidityThresholds));
        OnPropertyChanged(nameof(ValidityRedThresholdDays));
        OnPropertyChanged(nameof(ValidityOrangeThresholdDays));
        OnPropertyChanged(nameof(ValidityGreenThresholdDays));

        if (persist)
        {
            SchedulePersist();
        }
    }

    private void SchedulePersist()
    {
        if (!_initialized)
        {
            return;
        }

        if (_lifecycleService.RunToken.IsCancellationRequested)
        {
            _logger.LogDebug("Skipping hot state persistence because the application is stopping.");
            return;
        }

        CancellationTokenSource? previous;

        lock (_persistSync)
        {
            previous = _persistSource;
            var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleService.RunToken);
            _persistSource = linkedSource;
            _persistTask = RunPersistAsync(linkedSource);
        }

        previous?.Cancel();
        previous?.Dispose();
    }

    private async Task RunPersistAsync(CancellationTokenSource source)
    {
        var token = source.Token;

        try
        {
            if (PersistDebounceDelay > TimeSpan.Zero)
            {
                await Task.Delay(PersistDebounceDelay, token).ConfigureAwait(false);
            }

            await PersistStateAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _logger.LogDebug("Hot state persistence canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure while persisting hot state.");
            var message = "Nepodařilo se uložit poslední použitý stav.";
            if (!string.IsNullOrWhiteSpace(ex.Message))
            {
                message = $"{message} {ex.Message}";
            }

            _statusService.Error(message);
        }
        finally
        {
            CleanupPersistState(source);
        }
    }

    private async Task PersistStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await PersistStateInternalAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                var message = "Nepodařilo se uložit poslední použitý stav.";
                if (!string.IsNullOrWhiteSpace(result.Exception?.Message))
                {
                    message = $"{message} {result.Exception.Message}";
                }

                _statusService.Error(message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
        {
            _logger.LogDebug("Hot state persistence canceled by token.");
        }
    }

    private async Task<PersistStateResult> PersistStateInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _settingsService.UpdateAsync(settings =>
                {
                    settings.LastQuery = LastQuery;
                    settings.LastFolder = LastFolder;
                    settings.PageSize = PageSize > 0 ? PageSize : AppSettings.DefaultPageSize;

                    settings.Import ??= new ImportPreferences();
                    settings.Import.Recursive = ImportRecursive;
                    settings.Import.KeepFsMetadata = ImportKeepFsMetadata;
                    settings.Import.SetReadOnly = ImportSetReadOnly;
                    settings.Import.UseParallel = ImportUseParallel;
                    settings.Import.MaxDegreeOfParallelism = ImportMaxDegreeOfParallelism > 0
                        ? ImportMaxDegreeOfParallelism
                        : Environment.ProcessorCount;
                    settings.Import.DefaultAuthor = ImportDefaultAuthor;
                    settings.Import.MaxFileSizeMegabytes = ImportMaxFileSizeMegabytes.HasValue && ImportMaxFileSizeMegabytes.Value > 0
                        ? ImportMaxFileSizeMegabytes
                        : null;
                    settings.Import.AutoExportLog = ImportAutoExportLog;

                    settings.Validity ??= new ValidityPreferences();
                    settings.Validity.RedThresholdDays = ValidityRedThresholdDays;
                    settings.Validity.OrangeThresholdDays = ValidityOrangeThresholdDays;
                    settings.Validity.GreenThresholdDays = ValidityGreenThresholdDays;
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }

            return PersistStateResult.CreateSuccess();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist hot state.");
            return PersistStateResult.CreateFailure(ex);
        }
    }

    private void CleanupPersistState(CancellationTokenSource source)
    {
        lock (_persistSync)
        {
            if (ReferenceEquals(_persistSource, source))
            {
                _persistSource = null;
                _persistTask = null;
            }
        }

        source.Dispose();
    }

    private readonly struct PersistStateResult
    {
        private PersistStateResult(bool success, Exception? exception)
        {
            Success = success;
            Exception = exception;
        }

        public bool Success { get; }

        public Exception? Exception { get; }

        public static PersistStateResult CreateSuccess() => new(true, null);

        public static PersistStateResult CreateFailure(Exception exception) => new(false, exception);
    }
}
