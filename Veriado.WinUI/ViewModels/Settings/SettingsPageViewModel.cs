using System.Globalization;
using Veriado.WinUI.Localization;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Settings;

public partial class SettingsPageViewModel : ViewModelBase, IDisposable
{
    private readonly IHotStateService _hotStateService;
    private readonly IThemeService _themeService;
    private readonly AsyncRelayCommand _applyThemeCommand;
    private readonly AsyncRelayCommand _applyLanguageCommand;
    private readonly ILocalizationService _localizationService;

    public SettingsPageViewModel(
        IHotStateService hotStateService,
        IThemeService themeService,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler,
        ILocalizationService localizationService)
        : base(messenger, statusService, dispatcher, exceptionHandler, localizationService)
    {
        _hotStateService = hotStateService ?? throw new ArgumentNullException(nameof(hotStateService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        ThemeMode = _themeService.CurrentTheme;
        PageSize = _hotStateService.PageSize;
        LastFolder = _hotStateService.LastFolder;
        SelectedLanguage = _localizationService.CurrentCulture;

        ThemeOptions = Array.AsReadOnly(Enum.GetValues<AppTheme>());
        LanguageOptions = _localizationService.SupportedCultures;

        _applyThemeCommand = new AsyncRelayCommand(ApplyThemeAsync);
        _applyLanguageCommand = new AsyncRelayCommand(ApplyLanguageAsync, CanApplyLanguage);

        _localizationService.CultureChanged += OnCultureChanged;
    }

    public IReadOnlyList<AppTheme> ThemeOptions { get; }

    public IReadOnlyList<CultureInfo> LanguageOptions { get; }

    [ObservableProperty]
    private AppTheme themeMode;

    [ObservableProperty]
    private int pageSize;

    [ObservableProperty]
    private string? lastFolder;

    [ObservableProperty]
    private CultureInfo selectedLanguage = LocalizationConfiguration.DefaultCulture;

    public IAsyncRelayCommand ApplyThemeCommand => _applyThemeCommand;

    public IAsyncRelayCommand ApplyLanguageCommand => _applyLanguageCommand;

    private Task ApplyThemeAsync()
    {
        return SafeExecuteAsync(async cancellationToken =>
        {
            await _themeService.SetThemeAsync(ThemeMode, cancellationToken).ConfigureAwait(false);
            StatusService.Info(GetString("Settings.ThemeApplied"));
        });
    }

    private Task ApplyLanguageAsync()
    {
        if (SelectedLanguage is null)
        {
            return Task.CompletedTask;
        }

        return SafeExecuteAsync(cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var applied = _localizationService.TrySetCulture(SelectedLanguage);

            if (applied)
            {
                StatusService.Info(GetString("Settings.LanguageApplied", null, SelectedLanguage.NativeName));
            }

            return Task.CompletedTask;
        });
    }

    partial void OnPageSizeChanged(int value)
    {
        if (value <= 0)
        {
            StatusService.Error(GetString("Settings.PageSizeInvalid"));
            PageSize = _hotStateService.PageSize;
            return;
        }

        _hotStateService.PageSize = value;
        StatusService.Info(GetString("Settings.PageSizeUpdated", null, value));
    }

    partial void OnLastFolderChanged(string? value)
    {
        _hotStateService.LastFolder = value;
    }

    partial void OnSelectedLanguageChanged(CultureInfo value)
    {
        _applyLanguageCommand.NotifyCanExecuteChanged();
    }

    private bool CanApplyLanguage()
    {
        return SelectedLanguage is not null
            && !string.Equals(SelectedLanguage.Name, _localizationService.CurrentCulture.Name, StringComparison.OrdinalIgnoreCase);
    }

    private void OnCultureChanged(object? sender, CultureInfo culture)
    {
        if (!string.Equals(SelectedLanguage.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
        {
            SelectedLanguage = culture;
        }
    }

    public void Dispose()
    {
        _localizationService.CultureChanged -= OnCultureChanged;
        GC.SuppressFinalize(this);
    }
}
