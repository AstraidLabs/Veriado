using System.ComponentModel;
using Veriado.WinUI.Helpers;
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
        ValidityRedThreshold = _hotStateService.ValidityRedThresholdDays;
        ValidityOrangeThreshold = _hotStateService.ValidityOrangeThresholdDays;
        ValidityGreenThreshold = _hotStateService.ValidityGreenThresholdDays;

        ThemeOptions = Array.AsReadOnly(Enum.GetValues<AppTheme>());
        _applyThemeCommand = new AsyncRelayCommand(ApplyThemeAsync);
        if (_hotStateService is INotifyPropertyChanged observable)
        {
            observable.PropertyChanged += OnHotStatePropertyChanged;
        }
    }

    public IReadOnlyList<AppTheme> ThemeOptions { get; }

    [ObservableProperty]
    private AppTheme themeMode;

    [ObservableProperty]
    private int pageSize;

    [ObservableProperty]
    private string? lastFolder;

    [ObservableProperty]
    private int validityRedThreshold;

    [ObservableProperty]
    private int validityOrangeThreshold;

    [ObservableProperty]
    private int validityGreenThreshold;

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

    partial void OnValidityRedThresholdChanged(int value)
    {
        if (value < 0)
        {
            StatusService.Error("Zadejte nezáporný počet dní.");
            if (ValidityRedThreshold != _hotStateService.ValidityRedThresholdDays)
            {
                ValidityRedThreshold = _hotStateService.ValidityRedThresholdDays;
            }
            return;
        }

        if (_hotStateService.ValidityRedThresholdDays == value)
        {
            return;
        }

        _hotStateService.ValidityRedThresholdDays = value;
        var daysText = CzechPluralization.FormatDays(_hotStateService.ValidityRedThresholdDays);
        StatusService.Info($"Červený odznak se zobrazí {daysText} před expirací.");
    }

    partial void OnValidityOrangeThresholdChanged(int value)
    {
        if (value < 0)
        {
            StatusService.Error("Zadejte nezáporný počet dní.");
            if (ValidityOrangeThreshold != _hotStateService.ValidityOrangeThresholdDays)
            {
                ValidityOrangeThreshold = _hotStateService.ValidityOrangeThresholdDays;
            }

            return;
        }

        if (_hotStateService.ValidityOrangeThresholdDays == value)
        {
            return;
        }

        _hotStateService.ValidityOrangeThresholdDays = value;
        var daysText = CzechPluralization.FormatDays(_hotStateService.ValidityOrangeThresholdDays);
        StatusService.Info($"Oranžový odznak se zobrazí {daysText} před expirací.");
    }

    partial void OnValidityGreenThresholdChanged(int value)
    {
        if (value < 0)
        {
            StatusService.Error("Zadejte nezáporný počet dní.");
            if (ValidityGreenThreshold != _hotStateService.ValidityGreenThresholdDays)
            {
                ValidityGreenThreshold = _hotStateService.ValidityGreenThresholdDays;
            }

            return;
        }

        if (_hotStateService.ValidityGreenThresholdDays == value)
        {
            return;
        }

        _hotStateService.ValidityGreenThresholdDays = value;
        var daysText = CzechPluralization.FormatDays(_hotStateService.ValidityGreenThresholdDays);
        StatusService.Info($"Zelený odznak se zobrazí {daysText} před expirací.");
    }

    private void OnHotStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IHotStateService.ValidityRedThresholdDays)
            && ValidityRedThreshold != _hotStateService.ValidityRedThresholdDays)
        {
            ValidityRedThreshold = _hotStateService.ValidityRedThresholdDays;
        }
        else if (e.PropertyName is nameof(IHotStateService.ValidityOrangeThresholdDays)
            && ValidityOrangeThreshold != _hotStateService.ValidityOrangeThresholdDays)
        {
            ValidityOrangeThreshold = _hotStateService.ValidityOrangeThresholdDays;
        }
        else if (e.PropertyName is nameof(IHotStateService.ValidityGreenThresholdDays)
            && ValidityGreenThreshold != _hotStateService.ValidityGreenThresholdDays)
        {
            ValidityGreenThreshold = _hotStateService.ValidityGreenThresholdDays;
        }
    }
}
