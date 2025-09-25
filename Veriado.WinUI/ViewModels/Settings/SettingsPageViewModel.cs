using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Settings;

public partial class SettingsPageViewModel : ViewModelBase
{
    private readonly IHotStateService _hotStateService;
    private readonly IThemeService _themeService;
    private readonly AsyncRelayCommand _applyThemeCommand;

    public SettingsPageViewModel(
        IHotStateService hotStateService,
        IThemeService themeService,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _hotStateService = hotStateService ?? throw new ArgumentNullException(nameof(hotStateService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));

        ThemeMode = _themeService.CurrentTheme;
        PageSize = _hotStateService.PageSize;
        LastFolder = _hotStateService.LastFolder;

        ThemeOptions = Array.AsReadOnly(Enum.GetValues<AppTheme>());
        _applyThemeCommand = new AsyncRelayCommand(ApplyThemeAsync);
    }

    public IReadOnlyList<AppTheme> ThemeOptions { get; }

    [ObservableProperty]
    private AppTheme themeMode;

    [ObservableProperty]
    private int pageSize;

    [ObservableProperty]
    private string? lastFolder;

    public IAsyncRelayCommand ApplyThemeCommand => _applyThemeCommand;

    private Task ApplyThemeAsync()
    {
        return SafeExecuteAsync(async cancellationToken =>
        {
            await _themeService.SetThemeAsync(ThemeMode, cancellationToken).ConfigureAwait(false);
            StatusService.Info("Motiv byl použit.");
        });
    }

    partial void OnPageSizeChanged(int value)
    {
        if (value <= 0)
        {
            StatusService.Error("Zadejte kladnou velikost stránky.");
            PageSize = _hotStateService.PageSize;
            return;
        }

        _hotStateService.PageSize = value;
        StatusService.Info($"Velikost stránky nastavena na {value} položek.");
    }

    partial void OnLastFolderChanged(string? value)
    {
        _hotStateService.LastFolder = value;
    }
}
