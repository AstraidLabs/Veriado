using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed partial class HotStateService : ObservableObject, IHotStateService
{
    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    [ObservableProperty]
    private string? lastQuery;

    [ObservableProperty]
    private string? lastFolder;

    [ObservableProperty]
    private int pageSize = AppSettings.DefaultPageSize;

    public HotStateService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
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
        }
        catch
        {
            // Persistence of hot state should not crash the UI layer.
        }
    }
}
