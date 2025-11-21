using Veriado.Contracts.Storage;
using Veriado.Services.Storage;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Storage;

public partial class StorageManagementPageViewModel : ViewModelBase
{
    private readonly IStorageManagementService _storageService;
    private readonly IPickerService _pickerService;

    private readonly AsyncRelayCommand _refreshCommand;
    private readonly AsyncRelayCommand _runMigrationCommand;
    private readonly AsyncRelayCommand _runExportCommand;
    private readonly AsyncRelayCommand _runImportCommand;
    private readonly AsyncRelayCommand _pickMigrationTargetCommand;
    private readonly AsyncRelayCommand _pickExportRootCommand;
    private readonly AsyncRelayCommand _pickImportPackageRootCommand;
    private readonly AsyncRelayCommand _pickImportTargetRootCommand;

    public StorageManagementPageViewModel(
        IStorageManagementService storageService,
        IPickerService pickerService,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _pickerService = pickerService ?? throw new ArgumentNullException(nameof(pickerService));

        _refreshCommand = new AsyncRelayCommand(RefreshAsync, CanExecuteWhenIdle);
        _runMigrationCommand = new AsyncRelayCommand(RunMigrationAsync, CanRunMigration);
        _runExportCommand = new AsyncRelayCommand(RunExportAsync, CanRunExport);
        _runImportCommand = new AsyncRelayCommand(RunImportAsync, CanRunImport);
        _pickMigrationTargetCommand = new AsyncRelayCommand(PickMigrationTargetAsync, CanExecuteWhenIdle);
        _pickExportRootCommand = new AsyncRelayCommand(PickExportRootAsync, CanExecuteWhenIdle);
        _pickImportPackageRootCommand = new AsyncRelayCommand(PickImportPackageRootAsync, CanExecuteWhenIdle);
        _pickImportTargetRootCommand = new AsyncRelayCommand(PickImportTargetRootAsync, CanExecuteWhenIdle);
    }

    [ObservableProperty]
    private string? currentRoot;

    [ObservableProperty]
    private string? effectiveRoot;

    [ObservableProperty]
    private string? migrationTargetRoot;

    [ObservableProperty]
    private bool deleteSourceAfterCopy;

    [ObservableProperty]
    private bool verifyHashes;

    [ObservableProperty]
    private string? exportPackageRoot;

    [ObservableProperty]
    private bool exportOverwriteExisting;

    [ObservableProperty]
    private string? importPackageRoot;

    [ObservableProperty]
    private string? importTargetRoot;

    [ObservableProperty]
    private bool importOverwriteExisting;

    [ObservableProperty]
    private bool importVerifyAfterCopy;

    public IAsyncRelayCommand RefreshCommand => _refreshCommand;

    public IAsyncRelayCommand RunMigrationCommand => _runMigrationCommand;

    public IAsyncRelayCommand RunExportCommand => _runExportCommand;

    public IAsyncRelayCommand RunImportCommand => _runImportCommand;

    public IAsyncRelayCommand PickMigrationTargetCommand => _pickMigrationTargetCommand;

    public IAsyncRelayCommand PickExportRootCommand => _pickExportRootCommand;

    public IAsyncRelayCommand PickImportPackageRootCommand => _pickImportPackageRootCommand;

    public IAsyncRelayCommand PickImportTargetRootCommand => _pickImportTargetRootCommand;

    [RelayCommand]
    private void Cancel()
    {
        TryCancelRunning();
    }

    private Task RefreshAsync()
    {
        return SafeExecuteAsync(async cancellationToken =>
        {
            var current = await _storageService.GetCurrentRootAsync(cancellationToken).ConfigureAwait(false);
            var effective = await _storageService.GetEffectiveRootAsync(cancellationToken).ConfigureAwait(false);

            await Dispatcher.Enqueue(() =>
            {
                CurrentRoot = current;
                EffectiveRoot = effective;
            });
        }, "Načítám stav úložiště...");
    }

    private Task RunMigrationAsync()
    {
        return SafeExecuteAsync(async cancellationToken =>
        {
            if (string.IsNullOrWhiteSpace(MigrationTargetRoot))
            {
                StatusService.Error("Vyberte cílovou složku pro migraci.");
                return;
            }

            var options = new StorageMigrationOptionsDto
            {
                DeleteSourceAfterCopy = DeleteSourceAfterCopy,
                VerifyHashes = VerifyHashes,
            };

            var result = await _storageService
                .MigrateRootAsync(MigrationTargetRoot, options, cancellationToken)
                .ConfigureAwait(false);

            await Dispatcher.Enqueue(() =>
            {
                CurrentRoot = result.NewRoot;
                EffectiveRoot = result.NewRoot;
            });

            StatusService.Info($"Migrace dokončena. Přeneseno {result.MigratedFiles} souborů.");
        }, "Provádím migraci úložiště...");
    }

    private Task RunExportAsync()
    {
        return SafeExecuteAsync(async cancellationToken =>
        {
            if (string.IsNullOrWhiteSpace(ExportPackageRoot))
            {
                StatusService.Error("Vyberte cílovou složku pro export balíčku.");
                return;
            }

            var options = new StorageExportOptionsDto
            {
                OverwriteExisting = ExportOverwriteExisting,
            };

            var result = await _storageService
                .ExportAsync(ExportPackageRoot, options, cancellationToken)
                .ConfigureAwait(false);

            StatusService.Info($"Export dokončen. Databáze: {result.DatabasePath}.");
        }, "Exportuji databázi a dokumenty...");
    }

    private Task RunImportAsync()
    {
        return SafeExecuteAsync(async cancellationToken =>
        {
            if (string.IsNullOrWhiteSpace(ImportPackageRoot))
            {
                StatusService.Error("Vyberte složku s exportovaným balíčkem.");
                return;
            }

            if (string.IsNullOrWhiteSpace(ImportTargetRoot))
            {
                StatusService.Error("Vyberte cílovou složku pro import dat.");
                return;
            }

            var options = new StorageImportOptionsDto
            {
                OverwriteExisting = ImportOverwriteExisting,
                VerifyAfterCopy = ImportVerifyAfterCopy,
            };

            var result = await _storageService
                .ImportAsync(ImportPackageRoot, ImportTargetRoot, options, cancellationToken)
                .ConfigureAwait(false);

            await Dispatcher.Enqueue(() =>
            {
                CurrentRoot = result.TargetStorageRoot;
                EffectiveRoot = result.TargetStorageRoot;
            });

            StatusService.Info($"Import dokončen. Načteno {result.ImportedFiles} souborů.");
        }, "Importuji databázi a dokumenty...");
    }

    private Task PickMigrationTargetAsync()
    {
        return PickFolderAsync(value => MigrationTargetRoot = value);
    }

    private Task PickExportRootAsync()
    {
        return PickFolderAsync(value => ExportPackageRoot = value);
    }

    private Task PickImportPackageRootAsync()
    {
        return PickFolderAsync(value => ImportPackageRoot = value);
    }

    private Task PickImportTargetRootAsync()
    {
        return PickFolderAsync(value => ImportTargetRoot = value);
    }

    private Task PickFolderAsync(Action<string?> setter)
    {
        return SafeExecuteAsync(async cancellationToken =>
        {
            var folder = await _pickerService.PickFolderAsync().ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.Enqueue(() => setter(folder));
        }, cancellationToken: CancellationToken.None);
    }

    private bool CanExecuteWhenIdle()
        => !IsBusy;

    private bool CanRunMigration()
        => !IsBusy && !string.IsNullOrWhiteSpace(MigrationTargetRoot);

    private bool CanRunExport()
        => !IsBusy && !string.IsNullOrWhiteSpace(ExportPackageRoot);

    private bool CanRunImport()
        => !IsBusy
            && !string.IsNullOrWhiteSpace(ImportPackageRoot)
            && !string.IsNullOrWhiteSpace(ImportTargetRoot);

    partial void OnIsBusyChanged(bool value)
    {
        NotifyCommandStates();
    }

    partial void OnMigrationTargetRootChanged(string? value)
    {
        _runMigrationCommand.NotifyCanExecuteChanged();
    }

    partial void OnExportPackageRootChanged(string? value)
    {
        _runExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnImportPackageRootChanged(string? value)
    {
        _runImportCommand.NotifyCanExecuteChanged();
    }

    partial void OnImportTargetRootChanged(string? value)
    {
        _runImportCommand.NotifyCanExecuteChanged();
    }

    private void NotifyCommandStates()
    {
        _refreshCommand.NotifyCanExecuteChanged();
        _runMigrationCommand.NotifyCanExecuteChanged();
        _runExportCommand.NotifyCanExecuteChanged();
        _runImportCommand.NotifyCanExecuteChanged();
        _pickMigrationTargetCommand.NotifyCanExecuteChanged();
        _pickExportRootCommand.NotifyCanExecuteChanged();
        _pickImportPackageRootCommand.NotifyCanExecuteChanged();
        _pickImportTargetRootCommand.NotifyCanExecuteChanged();
    }
}
