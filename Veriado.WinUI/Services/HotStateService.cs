using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Veriado.Contracts.Files;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed partial class HotStateService : ObservableObject, IHotStateService
{
    private readonly ISettingsService _settingsService;
    private readonly IStatusService _statusService;
    private readonly ILogger<HotStateService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;
    private ValidityThresholds _validityThresholds = AppSettings.CreateDefaultValidityThresholds();

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
        ILogger<HotStateService> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
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

    partial void OnLastQueryChanged(string? value) => SchedulePersistAsync();

    partial void OnLastFolderChanged(string? value) => SchedulePersistAsync();

    partial void OnPageSizeChanged(int value)
    {
        if (value <= 0)
        {
            pageSize = AppSettings.DefaultPageSize;
            OnPropertyChanged(nameof(PageSize));
        }

        SchedulePersistAsync();
    }

    partial void OnImportRecursiveChanged(bool value) => SchedulePersistAsync();

    partial void OnImportKeepFsMetadataChanged(bool value) => SchedulePersistAsync();

    partial void OnImportSetReadOnlyChanged(bool value) => SchedulePersistAsync();

    partial void OnImportUseParallelChanged(bool value) => SchedulePersistAsync();

    partial void OnImportAutoExportLogChanged(bool value) => SchedulePersistAsync();

    partial void OnImportMaxDegreeOfParallelismChanged(int value)
    {
        if (value <= 0)
        {
            importMaxDegreeOfParallelism = Environment.ProcessorCount;
            OnPropertyChanged(nameof(ImportMaxDegreeOfParallelism));
        }

        SchedulePersistAsync();
    }

    partial void OnImportDefaultAuthorChanged(string? value) => SchedulePersistAsync();

    partial void OnImportMaxFileSizeMegabytesChanged(double? value)
    {
        if (value.HasValue && value.Value <= 0)
        {
            importMaxFileSizeMegabytes = null;
            OnPropertyChanged(nameof(ImportMaxFileSizeMegabytes));
        }

        SchedulePersistAsync();
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
            SchedulePersistAsync();
        }
    }

    private Task PersistAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            return Task.CompletedTask;
        }

        return PersistStateAsync(cancellationToken);
    }

    private void SchedulePersistAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            return;
        }

        _ = PersistAndLogAsync(cancellationToken);
    }

    private async Task PersistAndLogAsync(CancellationToken cancellationToken)
    {
        try
        {
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist hot state.");
        }
    }

    private async Task PersistStateAsync(CancellationToken cancellationToken)
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist hot state.");
            return PersistStateResult.CreateFailure(ex);
        }
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
