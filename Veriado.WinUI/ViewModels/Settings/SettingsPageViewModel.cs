using System.ComponentModel;
using Veriado.Contracts.Localization;
using Veriado.Services.Storage;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Settings;

public partial class SettingsPageViewModel : ViewModelBase
{
    private readonly IStorageSettingsService _storageSettingsService;
    private readonly IPickerService _pickerService;
    private readonly IHotStateService _hotStateService;
    private readonly IThemeService _themeService;
    private readonly AsyncRelayCommand _applyThemeCommand;
    private readonly AsyncRelayCommand _loadStorageSettingsCommand;
    private readonly AsyncRelayCommand _browseStorageRootCommand;
    private readonly AsyncRelayCommand _saveStorageRootCommand;

    public SettingsPageViewModel(
        IStorageSettingsService storageSettingsService,
        IPickerService pickerService,
        IHotStateService hotStateService,
        IThemeService themeService,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _storageSettingsService = storageSettingsService ?? throw new ArgumentNullException(nameof(storageSettingsService));
        _pickerService = pickerService ?? throw new ArgumentNullException(nameof(pickerService));
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
        _loadStorageSettingsCommand = new AsyncRelayCommand(LoadStorageSettingsAsync);
        _browseStorageRootCommand = new AsyncRelayCommand(BrowseStorageRootAsync, () => CanChangeStorageRoot);
        _saveStorageRootCommand = new AsyncRelayCommand(SaveStorageRootAsync, () => CanChangeStorageRoot);
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

    [ObservableProperty]
    private string? currentStorageRoot;

    [ObservableProperty]
    private bool canChangeStorageRoot;

    [ObservableProperty]
    private string? newStorageRootCandidate;

    [ObservableProperty]
    private string? storageChangeMessage;

    public IAsyncRelayCommand LoadStorageSettingsCommand => _loadStorageSettingsCommand;

    public IAsyncRelayCommand BrowseStorageRootCommand => _browseStorageRootCommand;

    public IAsyncRelayCommand SaveStorageRootCommand => _saveStorageRootCommand;

    private Task LoadStorageSettingsAsync()
    {
        return SafeExecuteAsync(async cancellationToken =>
        {
            var dto = await _storageSettingsService
                .GetStorageSettingsAsync(cancellationToken)
                .ConfigureAwait(false);

            CurrentStorageRoot = dto.CurrentRootPath;
            CanChangeStorageRoot = dto.CanChangeRoot;
            NewStorageRootCandidate = dto.CurrentRootPath;

            StorageChangeMessage = dto.CanChangeRoot
                ? null
                : "Pro změnu úložiště je nutná migrace.";
        });
    }

    private Task BrowseStorageRootAsync()
    {
        if (!CanChangeStorageRoot)
        {
            return Task.CompletedTask;
        }

        return SafeExecuteAsync(async _ =>
        {
            var folder = await _pickerService.PickFolderAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                NewStorageRootCandidate = folder;
            }
        });
    }

    private Task SaveStorageRootAsync()
    {
        if (!CanChangeStorageRoot || string.IsNullOrWhiteSpace(NewStorageRootCandidate))
        {
            return Task.CompletedTask;
        }

        return SafeExecuteAsync(async cancellationToken =>
        {
            var result = await _storageSettingsService
                .ChangeStorageRootAsync(NewStorageRootCandidate!, cancellationToken)
                .ConfigureAwait(false);

            switch (result)
            {
                case ChangeStorageRootResult.Success:
                    StorageChangeMessage = "Složka úložiště byla úspěšně změněna.";
                    CurrentStorageRoot = NewStorageRootCandidate;
                    break;

                case ChangeStorageRootResult.CatalogNotEmpty:
                    StorageChangeMessage = "Pro změnu úložiště je nutná migrace.";
                    CanChangeStorageRoot = false;
                    break;

                case ChangeStorageRootResult.InvalidPath:
                    StorageChangeMessage = "Neplatná cesta k úložišti. Zkontrolujte prosím složku.";
                    break;

                case ChangeStorageRootResult.IoError:
                    StorageChangeMessage = "Složku úložiště nelze použít kvůli chybě při přístupu na disk.";
                    break;

                default:
                    StorageChangeMessage = "Došlo k neočekávané chybě při změně úložiště.";
                    break;
            }

            _browseStorageRootCommand.NotifyCanExecuteChanged();
            _saveStorageRootCommand.NotifyCanExecuteChanged();
        });
    }

    private Task ApplyThemeAsync()
    {
        return SafeExecuteAsync(async cancellationToken =>
        {
            await _themeService.SetThemeAsync(ThemeMode, cancellationToken).ConfigureAwait(false);
            StatusService.Info("Motiv byl použit.");
        });
    }

    partial void OnCanChangeStorageRootChanged(bool value)
    {
        _browseStorageRootCommand.NotifyCanExecuteChanged();
        _saveStorageRootCommand.NotifyCanExecuteChanged();
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
