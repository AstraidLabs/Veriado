using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Files;
using Veriado.Services.Files;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Files;

public sealed partial class FileDetailViewModel : ViewModelBase
{
    private readonly IFileQueryService _queryService;
    private readonly IFileOperationsService _operations;
    private readonly IDialogService _dialogService;
    private readonly IPreviewService _previewService;
    private readonly IClipboardService _clipboardService;
    private readonly IShareService _shareService;

    [ObservableProperty]
    private FileDetailDto? detail;

    [ObservableProperty]
    private DateTimeOffset? validityIssuedAt;

    [ObservableProperty]
    private DateTimeOffset? validityValidUntil;

    [ObservableProperty]
    private bool validityHasPhysicalCopy;

    [ObservableProperty]
    private bool validityHasElectronicCopy;

    [ObservableProperty]
    private bool editableIsReadOnly;

    [ObservableProperty]
    private bool canEdit = true;

    [ObservableProperty]
    private string? editableName;

    [ObservableProperty]
    private string? editableAuthor;

    [ObservableProperty]
    private string? editableMime;

    [ObservableProperty]
    private string? contentPreview;

    public FileDetailViewModel(
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler,
        IFileQueryService queryService,
        IFileOperationsService operations,
        IDialogService dialogService,
        IPreviewService previewService,
        IClipboardService clipboardService,
        IShareService shareService)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _shareService = shareService ?? throw new ArgumentNullException(nameof(shareService));
    }

    [RelayCommand]
    private async Task LoadAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        await SafeExecuteAsync(ct => LoadCoreAsync(id, ct), "Načítám detail dokumentu…");
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (Detail is null || string.IsNullOrWhiteSpace(EditableName))
        {
            HasError = true;
            StatusService.Error("Zadejte nový název souboru.");
            return;
        }

        await SafeExecuteAsync(async ct =>
        {
            var response = await _operations
                .RenameAsync(Detail.Id, EditableName.Trim(), ct)
                .ConfigureAwait(false);

            if (!response.IsSuccess)
            {
                HasError = true;
                StatusService.Error("Přejmenování se nezdařilo.");
                return;
            }

            var fileId = response.Data ?? Detail.Id;
            await LoadCoreAsync(fileId, ct).ConfigureAwait(false);
            StatusService.Info("Název byl aktualizován.");
        }, "Aktualizuji název souboru…");
    }

    [RelayCommand]
    private async Task UpdateMetadataAsync()
    {
        if (Detail is null)
        {
            return;
        }

        await SafeExecuteAsync(async ct =>
        {
            var request = new UpdateMetadataRequest
            {
                FileId = Detail.Id,
                Author = string.IsNullOrWhiteSpace(EditableAuthor) ? null : EditableAuthor,
                Mime = string.IsNullOrWhiteSpace(EditableMime) ? null : EditableMime,
            };

            var response = await _operations.UpdateMetadataAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                HasError = true;
                StatusService.Error("Aktualizace metadat se nezdařila.");
                return;
            }

            var fileId = response.Data ?? Detail.Id;
            await LoadCoreAsync(fileId, ct).ConfigureAwait(false);
            StatusService.Info("Metadata byla aktualizována.");
        }, "Aktualizuji metadata…");
    }

    [RelayCommand]
    private async Task SetReadOnlyAsync(bool isReadOnly)
    {
        if (Detail is null)
        {
            return;
        }

        await SafeExecuteAsync(async ct =>
        {
            var response = await _operations
                .SetReadOnlyAsync(Detail.Id, isReadOnly, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccess)
            {
                HasError = true;
                StatusService.Error("Změna režimu se nezdařila.");
                return;
            }

            var fileId = response.Data ?? Detail.Id;
            await LoadCoreAsync(fileId, ct).ConfigureAwait(false);
            StatusService.Info(isReadOnly
                ? "Soubor je nyní jen pro čtení."
                : "Soubor lze znovu upravovat.");
        }, "Aktualizuji režim jen pro čtení…");
    }

    [RelayCommand]
    private async Task ApplyValidityAsync()
    {
        if (Detail is null)
        {
            return;
        }

        await SafeExecuteAsync(async ct =>
        {
            var issuedAt = ValidityIssuedAt ?? DateTimeOffset.UtcNow;
            var validUntil = ValidityValidUntil ?? issuedAt;
            var validity = new FileValidityDto(issuedAt, validUntil, ValidityHasPhysicalCopy, ValidityHasElectronicCopy);

            var response = await _operations
                .SetValidityAsync(Detail.Id, validity, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccess)
            {
                HasError = true;
                StatusService.Error("Uložení platnosti se nezdařilo.");
                return;
            }

            var fileId = response.Data ?? Detail.Id;
            await LoadCoreAsync(fileId, ct).ConfigureAwait(false);
            StatusService.Info("Platnost byla aktualizována.");
        }, "Ukládám platnost dokumentu…");
    }

    [RelayCommand]
    private async Task ClearValidityAsync()
    {
        if (Detail is null)
        {
            return;
        }

        var confirmed = await _dialogService
            .ConfirmAsync("Odebrat platnost", "Opravdu chcete odstranit platnost dokumentu?", "Odebrat", "Zrušit")
            .ConfigureAwait(false);

        if (!confirmed)
        {
            return;
        }

        await SafeExecuteAsync(async ct =>
        {
            var response = await _operations
                .ClearValidityAsync(Detail.Id, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccess)
            {
                HasError = true;
                StatusService.Error("Odstranění platnosti se nezdařilo.");
                return;
            }

            await LoadCoreAsync(Detail.Id, ct).ConfigureAwait(false);
            StatusService.Info("Platnost byla odstraněna.");
        }, "Odebírám platnost dokumentu…");
    }

    [RelayCommand]
    private async Task CopyIdAsync()
    {
        if (Detail is null)
        {
            return;
        }

        await _clipboardService.CopyTextAsync(Detail.Id.ToString());
        StatusService.Info("Identifikátor byl zkopírován do schránky.");
    }

    [RelayCommand]
    private async Task CopySnippetAsync()
    {
        if (string.IsNullOrWhiteSpace(ContentPreview))
        {
            return;
        }

        await _clipboardService.CopyTextAsync(ContentPreview);
        StatusService.Info("Ukázka obsahu byla zkopírována.");
    }

    [RelayCommand]
    private async Task ShareSnippetAsync()
    {
        if (Detail is null || string.IsNullOrWhiteSpace(ContentPreview))
        {
            return;
        }

        await _shareService.ShareTextAsync(Detail.Name ?? "Ukázka", ContentPreview);
    }

    private async Task LoadCoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _queryService.GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
        Detail = detail;

        if (detail is null)
        {
            HasError = true;
            ContentPreview = null;
            StatusService.Error("Dokument nebyl nalezen.");
            return;
        }

        EditableName = detail.Name;
        EditableAuthor = detail.Author;
        EditableMime = detail.Mime;
        EditableIsReadOnly = detail.IsReadOnly;
        CanEdit = true;

        if (detail.Validity is { } validity)
        {
            ValidityIssuedAt = validity.IssuedAt;
            ValidityValidUntil = validity.ValidUntil;
            ValidityHasPhysicalCopy = validity.HasPhysicalCopy;
            ValidityHasElectronicCopy = validity.HasElectronicCopy;
        }
        else
        {
            ValidityIssuedAt = null;
            ValidityValidUntil = null;
            ValidityHasPhysicalCopy = false;
            ValidityHasElectronicCopy = false;
        }

        var preview = await _previewService.GetPreviewAsync(detail.Id, cancellationToken).ConfigureAwait(false);
        ContentPreview = preview?.TextSnippet;
        HasError = false;
    }
}
