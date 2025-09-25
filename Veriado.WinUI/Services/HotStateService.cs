using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed partial class HotStateService : ObservableObject, IHotStateService
{
    private readonly ISettingsService _settingsService;
    private readonly IStatusService _statusService;
    private readonly ILogger<HotStateService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    [ObservableProperty]
    private partial string? lastQuery;

    [ObservableProperty]
    private partial string? lastFolder;

    [ObservableProperty]
    private partial int pageSize = AppSettings.DefaultPageSize;

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
