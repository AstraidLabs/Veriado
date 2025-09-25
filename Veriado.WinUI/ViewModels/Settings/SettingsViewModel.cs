using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Settings;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IThemeService _themeService;
    private readonly IHotStateService _hotState;

    public ObservableCollection<AppTheme> Themes { get; } = new(Enum.GetValues<AppTheme>());

    [ObservableProperty]
    private partial AppTheme selectedTheme;

    [ObservableProperty]
    private partial int pageSize;

    [ObservableProperty]
    private partial string? lastFolder;

    public SettingsViewModel(
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler,
        IThemeService themeService,
        IHotStateService hotState)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _hotState = hotState ?? throw new ArgumentNullException(nameof(hotState));

        SelectedTheme = _themeService.CurrentTheme;
        PageSize = _hotState.PageSize;
        LastFolder = _hotState.LastFolder;
    }

    partial void OnPageSizeChanged(int value)
    {
        var normalized = value <= 0 ? AppSettings.DefaultPageSize : value;
        if (normalized != value)
        {
            PageSize = normalized;
            return;
        }

        _hotState.PageSize = normalized;
        StatusService.Info($"Výchozí velikost stránky nastavena na {normalized}.");
    }

    partial void OnLastFolderChanged(string? value)
    {
        _hotState.LastFolder = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        if (_themeService.CurrentTheme == value)
        {
            return;
        }

        _ = ApplyThemeAsync(value);
    }

    private async Task ApplyThemeAsync(AppTheme theme)
    {
        await SafeExecuteAsync(async _ =>
        {
            await _themeService.SetThemeAsync(theme);
            StatusService.Info("Téma aplikace bylo aktualizováno.");
        });
    }
}
