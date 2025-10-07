using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Files;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Files;

public partial class FileDetailDialogViewModel : ViewModelBase
{
    private readonly Guid _fileId;
    private readonly IFileQueryService _fileQueryService;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly AsyncRelayCommand _saveMetadataCommand;
    private readonly AsyncRelayCommand _saveValidityCommand;
    private readonly AsyncRelayCommand _clearValidityCommand;

    private string? _originalMime;
    private string? _originalAuthor;
    private bool _originalIsReadOnly;
    private FileValidityDto? _originalValidity;

    public FileDetailDialogViewModel(
        FileSummaryDto summary,
        IFileQueryService fileQueryService,
        IFileOperationsService fileOperationsService,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        ArgumentNullException.ThrowIfNull(summary);
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _fileOperationsService = fileOperationsService ?? throw new ArgumentNullException(nameof(fileOperationsService));
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

        _saveMetadataCommand = new AsyncRelayCommand(SaveMetadataAsync, CanSaveMetadata);
        _saveValidityCommand = new AsyncRelayCommand(SaveValidityAsync, CanSaveValidity);
        _clearValidityCommand = new AsyncRelayCommand(ClearValidityAsync, CanClearValidity);
    }

    public event EventHandler? ChangesSaved;

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

    public IAsyncRelayCommand SaveMetadataCommand => _saveMetadataCommand;

    public IAsyncRelayCommand SaveValidityCommand => _saveValidityCommand;

    public IAsyncRelayCommand ClearValidityCommand => _clearValidityCommand;

    public string CreatedLocalText => FormatTimestamp(CreatedUtc);

    public string LastModifiedLocalText => FormatTimestamp(LastModifiedUtc);

    public string LastIndexedLocalText => LastIndexedUtc.HasValue
        ? FormatTimestamp(LastIndexedUtc.Value)
        : "–";

    public bool HasValidity => ValidityIssuedDate.HasValue && ValidityUntilDate.HasValue;

    public string ValiditySummaryText
    {
        get
        {
            if (!HasValidity)
            {
                return "Platnost nenastavena.";
            }

            var issued = ComposeLocalDateTime(ValidityIssuedDate!.Value, ValidityIssuedTime);
            var until = ComposeLocalDateTime(ValidityUntilDate!.Value, ValidityUntilTime);
            var issuedText = FormatTimestamp(issued);
            var untilText = FormatTimestamp(until);
            var flags = BuildCopyFlags();
            return string.IsNullOrEmpty(flags)
                ? $"Platí od {issuedText} do {untilText}."
                : $"Platí od {issuedText} do {untilText}. ({flags})";
        }
    }

    public Task InitializeAsync()
    {
        return SafeExecuteAsync(async token =>
        {
            var detail = await _fileQueryService.GetDetailAsync(_fileId, token).ConfigureAwait(false);
            if (detail is null)
            {
                StatusService.Error("Soubor se nepodařilo načíst.");
                return;
            }

            _originalMime = Normalize(detail.Mime);
            _originalAuthor = Normalize(detail.Author);
            _originalIsReadOnly = detail.IsReadOnly;
            _originalValidity = detail.Validity;

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
            });
        }, "Načítám detail souboru…");
    }

    partial void OnMimeChanged(string? value) => _saveMetadataCommand.NotifyCanExecuteChanged();

    partial void OnAuthorChanged(string? value) => _saveMetadataCommand.NotifyCanExecuteChanged();

    partial void OnIsReadOnlyChanged(bool value) => _saveMetadataCommand.NotifyCanExecuteChanged();

    partial void OnValidityIssuedDateChanged(DateTimeOffset? value)
    {
        UpdateValidityCommands();
        OnPropertyChanged(nameof(HasValidity));
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    partial void OnValidityIssuedTimeChanged(TimeSpan value)
    {
        UpdateValidityCommands();
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    partial void OnValidityUntilDateChanged(DateTimeOffset? value)
    {
        UpdateValidityCommands();
        OnPropertyChanged(nameof(HasValidity));
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    partial void OnValidityUntilTimeChanged(TimeSpan value)
    {
        UpdateValidityCommands();
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    partial void OnValidityHasPhysicalCopyChanged(bool value)
    {
        UpdateValidityCommands();
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    partial void OnValidityHasElectronicCopyChanged(bool value)
    {
        UpdateValidityCommands();
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    partial void OnCreatedUtcChanged(DateTimeOffset value) => OnPropertyChanged(nameof(CreatedLocalText));

    partial void OnLastModifiedUtcChanged(DateTimeOffset value) => OnPropertyChanged(nameof(LastModifiedLocalText));

    partial void OnLastIndexedUtcChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(LastIndexedLocalText));

    private bool CanSaveMetadata()
    {
        if (IsBusy)
        {
            return false;
        }

        var normalizedMime = Normalize(Mime);
        var normalizedAuthor = Normalize(Author);
        var mimeChanged = !string.Equals(normalizedMime, _originalMime, StringComparison.OrdinalIgnoreCase);
        var authorChanged = !string.Equals(normalizedAuthor, _originalAuthor, StringComparison.Ordinal);
        var readOnlyChanged = IsReadOnly != _originalIsReadOnly;
        return mimeChanged || authorChanged || readOnlyChanged;
    }

    private async Task SaveMetadataAsync()
    {
        await SafeExecuteAsync(async token =>
        {
            var normalizedMime = Normalize(Mime);
            var normalizedAuthor = Normalize(Author);
            var mimeChanged = !string.Equals(normalizedMime, _originalMime, StringComparison.OrdinalIgnoreCase);
            var authorChanged = !string.Equals(normalizedAuthor, _originalAuthor, StringComparison.Ordinal);
            var readOnlyChanged = IsReadOnly != _originalIsReadOnly;

            if (!mimeChanged && !authorChanged && !readOnlyChanged)
            {
                return;
            }

            var request = new UpdateMetadataRequest
            {
                FileId = _fileId,
                Mime = mimeChanged ? normalizedMime : null,
                Author = authorChanged ? normalizedAuthor : null,
                IsReadOnly = readOnlyChanged ? IsReadOnly : null,
            };

            var response = await _fileOperationsService.UpdateMetadataAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                var message = ExtractErrorMessage(response, "Nepodařilo se uložit vlastnosti souboru.");
                StatusService.Error(message);
                return;
            }

            _originalMime = normalizedMime;
            _originalAuthor = normalizedAuthor;
            _originalIsReadOnly = IsReadOnly;

            StatusService.Info("Vlastnosti souboru byly uloženy.");
            ChangesSaved?.Invoke(this, EventArgs.Empty);
        }, "Ukládám vlastnosti…");

        _saveMetadataCommand.NotifyCanExecuteChanged();
    }

    private bool CanSaveValidity()
    {
        if (IsBusy)
        {
            return false;
        }

        var candidate = BuildValidityCandidate(validateRange: false);
        if (candidate is null)
        {
            return false;
        }

        return !Equals(candidate, _originalValidity);
    }

    private async Task SaveValidityAsync()
    {
        await SafeExecuteAsync(async token =>
        {
            var candidate = BuildValidityCandidate(validateRange: true);
            if (candidate is null)
            {
                return;
            }

            var response = await _fileOperationsService.SetValidityAsync(_fileId, candidate, token).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                var message = ExtractErrorMessage(response, "Nepodařilo se uložit platnost dokumentu.");
                StatusService.Error(message);
                return;
            }

            _originalValidity = candidate;
            await Dispatcher.Enqueue(() => ApplyValidity(candidate));

            StatusService.Info("Platnost dokumentu byla uložena.");
            ChangesSaved?.Invoke(this, EventArgs.Empty);
        }, "Ukládám platnost…");

        UpdateValidityCommands();
    }

    private bool CanClearValidity()
    {
        if (IsBusy)
        {
            return false;
        }

        return _originalValidity is not null
            || ValidityIssuedDate.HasValue
            || ValidityUntilDate.HasValue
            || ValidityHasPhysicalCopy
            || ValidityHasElectronicCopy;
    }

    private async Task ClearValidityAsync()
    {
        await SafeExecuteAsync(async token =>
        {
            if (_originalValidity is null
                && !ValidityIssuedDate.HasValue
                && !ValidityUntilDate.HasValue
                && !ValidityHasPhysicalCopy
                && !ValidityHasElectronicCopy)
            {
                return;
            }

            var response = await _fileOperationsService.ClearValidityAsync(_fileId, token).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                var message = ExtractErrorMessage(response, "Nepodařilo se zrušit platnost dokumentu.");
                StatusService.Error(message);
                return;
            }

            _originalValidity = null;
            await Dispatcher.Enqueue(() => ApplyValidity(null));

            StatusService.Info("Platnost dokumentu byla zrušena.");
            ChangesSaved?.Invoke(this, EventArgs.Empty);
        }, "Ruším platnost…");

        UpdateValidityCommands();
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
            var issuedLocal = validity.IssuedAt.ToLocalTime();
            var untilLocal = validity.ValidUntil.ToLocalTime();

            ValidityIssuedDate = new DateTimeOffset(issuedLocal.Date, issuedLocal.Offset);
            ValidityIssuedTime = issuedLocal.TimeOfDay;
            ValidityUntilDate = new DateTimeOffset(untilLocal.Date, untilLocal.Offset);
            ValidityUntilTime = untilLocal.TimeOfDay;
            ValidityHasPhysicalCopy = validity.HasPhysicalCopy;
            ValidityHasElectronicCopy = validity.HasElectronicCopy;
        }

        OnPropertyChanged(nameof(HasValidity));
        OnPropertyChanged(nameof(ValiditySummaryText));
    }

    private void UpdateValidityCommands()
    {
        _saveValidityCommand.NotifyCanExecuteChanged();
        _clearValidityCommand.NotifyCanExecuteChanged();
    }

    private FileValidityDto? BuildValidityCandidate(bool validateRange)
    {
        if (!ValidityIssuedDate.HasValue || !ValidityUntilDate.HasValue)
        {
            return null;
        }

        var issuedLocal = ComposeLocalDateTime(ValidityIssuedDate.Value, ValidityIssuedTime);
        var untilLocal = ComposeLocalDateTime(ValidityUntilDate.Value, ValidityUntilTime);

        if (validateRange && untilLocal < issuedLocal)
        {
            StatusService.Error("Datum konce platnosti musí následovat po začátku platnosti.");
            return null;
        }

        return new FileValidityDto(
            issuedLocal.ToUniversalTime(),
            untilLocal.ToUniversalTime(),
            ValidityHasPhysicalCopy,
            ValidityHasElectronicCopy);
    }

    private static DateTimeOffset ComposeLocalDateTime(DateTimeOffset date, TimeSpan time)
    {
        return new DateTimeOffset(
            date.Year,
            date.Month,
            date.Day,
            time.Hours,
            time.Minutes,
            time.Seconds,
            date.Offset);
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

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
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

}
