using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Files;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Files;

public enum FileDetailDialogState
{
    Idle,
    Loading,
    Loaded,
    Saving,
    Error,
}

public partial class FileDetailDialogViewModel : ViewModelBase, INotifyDataErrorInfo
{
    private static readonly Regex MimeRegex = new(@"^[^/\s\\]+/[^/\s\\]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Guid _fileId;
    private readonly IFileQueryService _fileQueryService;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly ITimeFormattingService _timeFormattingService;
    private readonly AsyncRelayCommand _saveMetadataCommand;
    private readonly AsyncRelayCommand _saveValidityCommand;
    private readonly AsyncRelayCommand _clearValidityCommand;
    private readonly Dictionary<string, List<string>> _validationErrors = new(StringComparer.OrdinalIgnoreCase);

    private MetadataSnapshot _originalMetadata;
    private ValiditySnapshot? _originalValidity;
    private bool _isInitialized;

    public FileDetailDialogViewModel(
        FileSummaryDto summary,
        IFileQueryService fileQueryService,
        IFileOperationsService fileOperationsService,
        ITimeFormattingService timeFormattingService,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        ArgumentNullException.ThrowIfNull(summary);
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _fileOperationsService = fileOperationsService ?? throw new ArgumentNullException(nameof(fileOperationsService));
        _timeFormattingService = timeFormattingService ?? throw new ArgumentNullException(nameof(timeFormattingService));
        _fileId = summary.Id;

        DisplayName = BuildDisplayName(summary);
        Title = summary.Title;
        Mime = summary.Mime;
        Author = summary.Author;
        IsReadOnly = summary.IsReadOnly;
        Size = summary.Size;
        Version = summary.Version;
        CreatedUtc = summary.CreatedUtc;
        LastModifiedUtc = summary.LastModifiedUtc;
        IsIndexStale = summary.IsIndexStale;
        LastIndexedUtc = summary.LastIndexedUtc;
        IndexedTitle = summary.IndexedTitle;
        IndexSchemaVersion = summary.IndexSchemaVersion;
        IndexedContentHash = summary.IndexedContentHash;

        _originalMetadata = CaptureMetadataSnapshot();
        _originalValidity = null;

        _saveMetadataCommand = new AsyncRelayCommand(SaveMetadataAsync, CanSaveMetadata);
        _saveValidityCommand = new AsyncRelayCommand(SaveValidityAsync, CanSaveValidity);
        _clearValidityCommand = new AsyncRelayCommand(ClearValidityAsync, CanClearValidity);
    }

    public event EventHandler? ChangesSaved;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private string? title;

    [ObservableProperty]
    private string? mime;

    [ObservableProperty]
    private string? author;

    [ObservableProperty]
    private bool isReadOnly;

    [ObservableProperty]
    private long size;

    [ObservableProperty]
    private int version;

    [ObservableProperty]
    private DateTimeOffset createdUtc;

    [ObservableProperty]
    private DateTimeOffset lastModifiedUtc;

    [ObservableProperty]
    private bool isIndexStale;

    [ObservableProperty]
    private DateTimeOffset? lastIndexedUtc;

    [ObservableProperty]
    private string? indexedTitle;

    [ObservableProperty]
    private int indexSchemaVersion;

    [ObservableProperty]
    private string? indexedContentHash;

    [ObservableProperty]
    private DateTimeOffset? validityIssuedDate;

    [ObservableProperty]
    private TimeSpan validityIssuedTime = TimeSpan.Zero;

    [ObservableProperty]
    private DateTimeOffset? validityUntilDate;

    [ObservableProperty]
    private TimeSpan validityUntilTime = TimeSpan.Zero;

    [ObservableProperty]
    private bool validityHasPhysicalCopy;

    [ObservableProperty]
    private bool validityHasElectronicCopy;

    [ObservableProperty]
    private FileDetailDialogState state = FileDetailDialogState.Idle;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isMetadataDirty;

    [ObservableProperty]
    private bool isValidityDirty;

    [ObservableProperty]
    private bool hasPersistedChanges;

    public IAsyncRelayCommand SaveMetadataCommand => _saveMetadataCommand;

    public IAsyncRelayCommand SaveValidityCommand => _saveValidityCommand;

    public IAsyncRelayCommand ClearValidityCommand => _clearValidityCommand;

    public string CreatedLocalText => _timeFormattingService.Format(CreatedUtc);

    public string LastModifiedLocalText => _timeFormattingService.Format(LastModifiedUtc);

    public string LastIndexedLocalText => _timeFormattingService.FormatOrDash(LastIndexedUtc);

    public bool HasValidity => ValidityIssuedDate.HasValue && ValidityUntilDate.HasValue;

    public bool IsInteractionEnabled => !IsBusy && State is not FileDetailDialogState.Loading and not FileDetailDialogState.Saving;

    public string ValiditySummaryText
    {
        get
        {
            if (!HasValidity)
            {
                return "Platnost nenastavena.";
            }

            var candidate = BuildValiditySnapshot(validateCompleteness: false);
            if (candidate is null)
            {
                return "Platnost nenastavena.";
            }

            var issuedText = _timeFormattingService.Format(candidate.Value.IssuedUtc);
            var untilText = _timeFormattingService.Format(candidate.Value.ValidUntilUtc);
            var flags = BuildCopyFlags();
            return string.IsNullOrEmpty(flags)
                ? $"Platí od {issuedText} do {untilText}."
                : $"Platí od {issuedText} do {untilText}. ({flags})";
        }
    }

    public bool HasErrors => _validationErrors.Count > 0;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        State = FileDetailDialogState.Loading;
        return SafeExecuteAsync(async token =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
            var detail = await _fileQueryService.GetDetailAsync(_fileId, linked.Token).ConfigureAwait(false);
            if (detail is null)
            {
                await Dispatcher.Enqueue(() =>
                {
                    ErrorMessage = "Soubor se nepodařilo načíst.";
                    State = FileDetailDialogState.Error;
                });
                return;
            }

            var metadataSnapshot = new MetadataSnapshot(Normalize(detail.Mime), Normalize(detail.Author), detail.IsReadOnly);
            ValiditySnapshot? validitySnapshot = detail.Validity is null ? null : ValiditySnapshot.From(detail.Validity);

            await Dispatcher.Enqueue(() =>
            {
                Title = detail.Title;
                Mime = detail.Mime;
                Author = detail.Author;
                IsReadOnly = detail.IsReadOnly;
                Size = detail.Size;
                Version = detail.Version;
                CreatedUtc = detail.CreatedUtc;
                LastModifiedUtc = detail.LastModifiedUtc;
                IsIndexStale = detail.IsIndexStale;
                LastIndexedUtc = detail.LastIndexedUtc;
                IndexedTitle = detail.IndexedTitle;
                IndexSchemaVersion = detail.IndexSchemaVersion;
                IndexedContentHash = detail.IndexedContentHash;
                ApplyValidity(detail.Validity);

                _originalMetadata = metadataSnapshot;
                _originalValidity = validitySnapshot;
                _isInitialized = true;
                HasPersistedChanges = false;
                ErrorMessage = null;
                State = FileDetailDialogState.Loaded;

                ValidateMetadata();
                ValidateValidity();
                UpdateDirtyFlags();
            });
        }, "Načítám detail souboru…", cancellationToken);
    }

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return _validationErrors.Values.SelectMany(static x => x);
        }

        if (_validationErrors.TryGetValue(propertyName, out var errors))
        {
            return errors;
        }

        return Array.Empty<string>();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInteractionEnabled));
        UpdateCommandStates();
    }

    partial void OnStateChanged(FileDetailDialogState value)
    {
        OnPropertyChanged(nameof(IsInteractionEnabled));
        UpdateCommandStates();
    }

    partial void OnErrorMessageChanged(string? value)
    {
        HasError = !string.IsNullOrWhiteSpace(value);
    }

    partial void OnMimeChanged(string? value)
    {
        ValidateMime();
        UpdateMetadataDirtyState();
    }

    partial void OnAuthorChanged(string? value)
    {
        ValidateAuthor();
        UpdateMetadataDirtyState();
    }

    partial void OnIsReadOnlyChanged(bool value)
    {
        UpdateMetadataDirtyState();
    }

    partial void OnValidityIssuedDateChanged(DateTimeOffset? value)
    {
        ValidateValidity();
        UpdateValidityDirtyState();
        OnPropertyChanged(nameof(HasValidity));
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    partial void OnValidityIssuedTimeChanged(TimeSpan value)
    {
        ValidateValidity();
        UpdateValidityDirtyState();
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    partial void OnValidityUntilDateChanged(DateTimeOffset? value)
    {
        ValidateValidity();
        UpdateValidityDirtyState();
        OnPropertyChanged(nameof(HasValidity));
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    partial void OnValidityUntilTimeChanged(TimeSpan value)
    {
        ValidateValidity();
        UpdateValidityDirtyState();
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    partial void OnValidityHasPhysicalCopyChanged(bool value)
    {
        UpdateValidityDirtyState();
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    partial void OnValidityHasElectronicCopyChanged(bool value)
    {
        UpdateValidityDirtyState();
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    partial void OnCreatedUtcChanged(DateTimeOffset value) => OnPropertyChanged(nameof(CreatedLocalText));

    partial void OnLastModifiedUtcChanged(DateTimeOffset value) => OnPropertyChanged(nameof(LastModifiedLocalText));

    partial void OnLastIndexedUtcChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(LastIndexedLocalText));

    private bool CanSaveMetadata()
    {
        return _isInitialized && !IsBusy && !HasErrors && IsMetadataDirty;
    }

    private async Task SaveMetadataAsync()
    {
        if (!CanSaveMetadata())
        {
            return;
        }

        await SafeExecuteAsync(async token =>
        {
            State = FileDetailDialogState.Saving;
            ErrorMessage = null;

            var patch = BuildMetadataPatch();
            if (patch is null)
            {
                State = FileDetailDialogState.Loaded;
                return;
            }

            var response = await _fileOperationsService
                .UpdateMetadataAsync(_fileId, patch, Version, token)
                .ConfigureAwait(false);

            if (!response.IsSuccess)
            {
                var message = ExtractErrorMessage(response, "Nepodařilo se uložit vlastnosti souboru.");
                await Dispatcher.Enqueue(() =>
                {
                    ErrorMessage = message;
                    State = FileDetailDialogState.Error;
                    StatusService.Error(message);
                });
                return;
            }

            await Dispatcher.Enqueue(() =>
            {
                _originalMetadata = CaptureMetadataSnapshot();
                IsMetadataDirty = false;
                HasPersistedChanges = true;
                Version += 1;
                ErrorMessage = null;
                State = FileDetailDialogState.Loaded;
                StatusService.Info("Vlastnosti souboru byly uloženy.");
                ChangesSaved?.Invoke(this, EventArgs.Empty);
            });
        }, "Ukládám vlastnosti…");
    }

    private bool CanSaveValidity()
    {
        return _isInitialized && !IsBusy && !HasErrors && IsValidityDirty;
    }

    private async Task SaveValidityAsync()
    {
        if (!CanSaveValidity())
        {
            return;
        }

        await SafeExecuteAsync(async token =>
        {
            State = FileDetailDialogState.Saving;
            ErrorMessage = null;

            var candidate = BuildValiditySnapshot(validateCompleteness: true);
            if (candidate is null)
            {
                State = FileDetailDialogState.Loaded;
                return;
            }

            var dto = new FileValidityDto(
                candidate.Value.IssuedUtc,
                candidate.Value.ValidUntilUtc,
                candidate.Value.HasPhysicalCopy,
                candidate.Value.HasElectronicCopy);

            var response = await _fileOperationsService
                .SetValidityAsync(_fileId, dto, Version, token)
                .ConfigureAwait(false);

            if (!response.IsSuccess)
            {
                var message = ExtractErrorMessage(response, "Nepodařilo se uložit platnost dokumentu.");
                await Dispatcher.Enqueue(() =>
                {
                    ErrorMessage = message;
                    State = FileDetailDialogState.Error;
                    StatusService.Error(message);
                });
                return;
            }

            await Dispatcher.Enqueue(() =>
            {
                _originalValidity = candidate;
                ApplyValidity(dto);
                IsValidityDirty = false;
                HasPersistedChanges = true;
                Version += 1;
                ErrorMessage = null;
                State = FileDetailDialogState.Loaded;
                StatusService.Info("Platnost dokumentu byla uložena.");
                ChangesSaved?.Invoke(this, EventArgs.Empty);
            });
        }, "Ukládám platnost…");
    }

    private bool CanClearValidity()
    {
        return _isInitialized && !IsBusy && (IsValidityDirty || _originalValidity is not null);
    }

    private async Task ClearValidityAsync()
    {
        if (!CanClearValidity())
        {
            return;
        }

        await SafeExecuteAsync(async token =>
        {
            State = FileDetailDialogState.Saving;
            ErrorMessage = null;

            var response = await _fileOperationsService
                .ClearValidityAsync(_fileId, Version, token)
                .ConfigureAwait(false);

            if (!response.IsSuccess)
            {
                var message = ExtractErrorMessage(response, "Nepodařilo se zrušit platnost dokumentu.");
                await Dispatcher.Enqueue(() =>
                {
                    ErrorMessage = message;
                    State = FileDetailDialogState.Error;
                    StatusService.Error(message);
                });
                return;
            }

            await Dispatcher.Enqueue(() =>
            {
                _originalValidity = null;
                ApplyValidity(null);
                IsValidityDirty = false;
                HasPersistedChanges = true;
                Version += 1;
                ErrorMessage = null;
                State = FileDetailDialogState.Loaded;
                StatusService.Info("Platnost dokumentu byla zrušena.");
                ChangesSaved?.Invoke(this, EventArgs.Empty);
            });
        }, "Ruším platnost…");
    }

    private void ApplyValidity(FileValidityDto? validity)
    {
        if (validity is null)
        {
            ValidityIssuedDate = null;
            ValidityIssuedTime = TimeSpan.Zero;
            ValidityUntilDate = null;
            ValidityUntilTime = TimeSpan.Zero;
            ValidityHasPhysicalCopy = false;
            ValidityHasElectronicCopy = false;
        }
        else
        {
            var issued = _timeFormattingService.Split(validity.IssuedAt);
            var until = _timeFormattingService.Split(validity.ValidUntil);

            ValidityIssuedDate = issued.Date;
            ValidityIssuedTime = issued.TimeOfDay;
            ValidityUntilDate = until.Date;
            ValidityUntilTime = until.TimeOfDay;
            ValidityHasPhysicalCopy = validity.HasPhysicalCopy;
            ValidityHasElectronicCopy = validity.HasElectronicCopy;
        }

        OnPropertyChanged(nameof(HasValidity));
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    private void UpdateCommandStates()
    {
        _saveMetadataCommand.NotifyCanExecuteChanged();
        _saveValidityCommand.NotifyCanExecuteChanged();
        _clearValidityCommand.NotifyCanExecuteChanged();
    }

    private void UpdateDirtyFlags()
    {
        UpdateMetadataDirtyState();
        UpdateValidityDirtyState();
    }

    private void UpdateMetadataDirtyState()
    {
        if (!_isInitialized)
        {
            return;
        }

        IsMetadataDirty = !CaptureMetadataSnapshot().Equals(_originalMetadata);
    }

    private void UpdateValidityDirtyState()
    {
        if (!_isInitialized)
        {
            return;
        }

        var candidate = BuildValiditySnapshot(validateCompleteness: false);
        if (_originalValidity is null && candidate is null)
        {
            IsValidityDirty = false;
            return;
        }

        IsValidityDirty = !Equals(candidate, _originalValidity);
    }

    private MetadataSnapshot CaptureMetadataSnapshot()
    {
        return new MetadataSnapshot(Normalize(Mime), Normalize(Author), IsReadOnly);
    }

    private FileMetadataPatchDto? BuildMetadataPatch()
    {
        var current = CaptureMetadataSnapshot();
        var original = _originalMetadata;

        if (current.Equals(original))
        {
            return null;
        }

        return new FileMetadataPatchDto
        {
            Mime = !string.Equals(current.Mime, original.Mime, StringComparison.OrdinalIgnoreCase) ? current.Mime : null,
            Author = !string.Equals(current.Author, original.Author, StringComparison.Ordinal) ? current.Author : null,
            IsReadOnly = current.IsReadOnly != original.IsReadOnly ? current.IsReadOnly : null,
        };
    }

    private ValiditySnapshot? BuildValiditySnapshot(bool validateCompleteness)
    {
        if (!ValidityIssuedDate.HasValue && !ValidityUntilDate.HasValue)
        {
            return null;
        }

        if (!ValidityIssuedDate.HasValue || !ValidityUntilDate.HasValue)
        {
            if (validateCompleteness)
            {
                var message = "Vyplňte oba datumy platnosti.";
                SetErrors(nameof(ValidityIssuedDate), ValidityIssuedDate.HasValue ? Array.Empty<string>() : new[] { message });
                SetErrors(nameof(ValidityUntilDate), ValidityUntilDate.HasValue ? Array.Empty<string>() : new[] { message });
                ErrorMessage = message;
            }

            return null;
        }

        ClearErrors(nameof(ValidityIssuedDate));
        ClearErrors(nameof(ValidityUntilDate));

        var issuedUtc = _timeFormattingService.ComposeUtc(ValidityIssuedDate.Value, ValidityIssuedTime);
        var untilUtc = _timeFormattingService.ComposeUtc(ValidityUntilDate.Value, ValidityUntilTime);

        if (validateCompleteness && untilUtc < issuedUtc)
        {
            const string rangeMessage = "Datum konce platnosti musí následovat po začátku platnosti.";
            SetErrors(nameof(ValidityUntilDate), new[] { rangeMessage });
            ErrorMessage = rangeMessage;
            return null;
        }

        return new ValiditySnapshot(issuedUtc, untilUtc, ValidityHasPhysicalCopy, ValidityHasElectronicCopy);
    }

    private void ValidateMetadata()
    {
        ValidateMime();
        ValidateAuthor();
    }

    private void ValidateMime()
    {
        var normalized = Normalize(Mime);
        if (string.IsNullOrEmpty(normalized))
        {
            ClearErrors(nameof(Mime));
            return;
        }

        if (!MimeRegex.IsMatch(normalized))
        {
            SetErrors(nameof(Mime), new[] { "Zadejte platný MIME typ ve formátu typ/podtyp." });
        }
        else
        {
            ClearErrors(nameof(Mime));
        }
    }

    private void ValidateAuthor()
    {
        var normalized = Normalize(Author);
        if (string.IsNullOrEmpty(normalized))
        {
            ClearErrors(nameof(Author));
            return;
        }

        if (normalized.Length > 200)
        {
            SetErrors(nameof(Author), new[] { "Autor může mít maximálně 200 znaků." });
        }
        else
        {
            ClearErrors(nameof(Author));
        }
    }

    private void ValidateValidity()
    {
        var snapshot = BuildValiditySnapshot(validateCompleteness: false);
        if (snapshot is null)
        {
            ClearErrors(nameof(ValidityIssuedDate));
            ClearErrors(nameof(ValidityUntilDate));
            return;
        }

        if (snapshot.Value.ValidUntilUtc < snapshot.Value.IssuedUtc)
        {
            const string rangeMessage = "Datum konce platnosti musí následovat po začátku platnosti.";
            SetErrors(nameof(ValidityUntilDate), new[] { rangeMessage });
        }
        else
        {
            ClearErrors(nameof(ValidityIssuedDate));
            ClearErrors(nameof(ValidityUntilDate));
        }
    }

    private void SetErrors(string propertyName, IEnumerable<string> errors)
    {
        var errorList = errors.ToList();
        if (errorList.Count == 0)
        {
            ClearErrors(propertyName);
            return;
        }

        _validationErrors[propertyName] = errorList;
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnPropertyChanged(nameof(HasErrors));
        UpdateCommandStates();
    }

    private void ClearErrors(string propertyName)
    {
        if (_validationErrors.Remove(propertyName))
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(HasErrors));
            if (_validationErrors.Count == 0 && State != FileDetailDialogState.Error)
            {
                ErrorMessage = null;
            }
            UpdateCommandStates();
        }
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string BuildDisplayName(FileSummaryDto summary)
    {
        if (string.IsNullOrWhiteSpace(summary.Extension))
        {
            return summary.Name;
        }

        return $"{summary.Name}.{summary.Extension}";
    }

    private static string ExtractErrorMessage(ApiResponse<Guid> response, string fallback)
    {
        if (response.Errors is { Count: > 0 })
        {
            var message = response.Errors[0].Message;
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }

        return fallback;
    }

    private string BuildCopyFlags()
    {
        var flags = new[]
        {
            ValidityHasPhysicalCopy ? "fyzická kopie" : null,
            ValidityHasElectronicCopy ? "elektronická kopie" : null,
        };

        var filtered = flags.Where(static flag => !string.IsNullOrWhiteSpace(flag)).ToArray();
        return filtered.Length == 0 ? string.Empty : string.Join(", ", filtered);
    }

    private readonly record struct MetadataSnapshot(string? Mime, string? Author, bool IsReadOnly);

    private readonly record struct ValiditySnapshot(
        DateTimeOffset IssuedUtc,
        DateTimeOffset ValidUntilUtc,
        bool HasPhysicalCopy,
        bool HasElectronicCopy)
    {
        public static ValiditySnapshot From(FileValidityDto dto)
        {
            return new ValiditySnapshot(dto.IssuedAt, dto.ValidUntil, dto.HasPhysicalCopy, dto.HasElectronicCopy);
        }
    }
}
