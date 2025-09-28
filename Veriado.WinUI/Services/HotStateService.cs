using Microsoft.Extensions.Logging;

namespace Veriado.WinUI.Services;

public sealed partial class HotStateService : ObservableObject, IHotStateService
{
    private readonly ISettingsService _settingsService;
    private readonly IStatusService _statusService;
    private readonly ILogger<HotStateService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

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

            OnPropertyChanged(nameof(ImportRecursive));
            OnPropertyChanged(nameof(ImportKeepFsMetadata));
            OnPropertyChanged(nameof(ImportSetReadOnly));
            OnPropertyChanged(nameof(ImportUseParallel));
            OnPropertyChanged(nameof(ImportMaxDegreeOfParallelism));
            OnPropertyChanged(nameof(ImportDefaultAuthor));
            OnPropertyChanged(nameof(ImportMaxFileSizeMegabytes));
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    partial void OnLastQueryChanged(string? value) => PersistAsync();

    partial void OnLastFolderChanged(string? value) => PersistAsync();

    partial void OnPageSizeChanged(int value)
    {
        if (value <= 0)
        {
            pageSize = AppSettings.DefaultPageSize;
            OnPropertyChanged(nameof(PageSize));
        }

        PersistAsync();
    }

    partial void OnImportRecursiveChanged(bool value) => PersistAsync();

    partial void OnImportKeepFsMetadataChanged(bool value) => PersistAsync();

    partial void OnImportSetReadOnlyChanged(bool value) => PersistAsync();

    partial void OnImportUseParallelChanged(bool value) => PersistAsync();

    partial void OnImportMaxDegreeOfParallelismChanged(int value)
    {
        if (value <= 0)
        {
            importMaxDegreeOfParallelism = Environment.ProcessorCount;
            OnPropertyChanged(nameof(ImportMaxDegreeOfParallelism));
        }

        PersistAsync();
    }

    partial void OnImportDefaultAuthorChanged(string? value) => PersistAsync();

    partial void OnImportMaxFileSizeMegabytesChanged(double? value)
    {
        if (value.HasValue && value.Value <= 0)
        {
            importMaxFileSizeMegabytes = null;
            OnPropertyChanged(nameof(ImportMaxFileSizeMegabytes));
        }

        PersistAsync();
    }

    private void PersistAsync()
    {
        if (!_initialized)
        {
            return;
        }

        _ = PersistStateAsync();
    }

    private async Task PersistStateAsync()
    {
        var result = await PersistStateInternalAsync().ConfigureAwait(false);
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

    private async Task<PersistStateResult> PersistStateInternalAsync()
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
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
                }).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }

            return PersistStateResult.CreateSuccess();
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
