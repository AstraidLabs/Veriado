using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Files;
using Veriado.Services.Files;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Files;

public sealed partial class FileDetailViewModel : ViewModelBase
{
    private readonly IFileQueryService _queryService;
    private readonly IFileOperationsService _operations;

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

    public FileDetailViewModel(IMessenger messenger, IFileQueryService queryService, IFileOperationsService operations)
        : base(messenger)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
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
            StatusMessage = "Zadejte nový název souboru.";
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
                StatusMessage = "Přejmenování se nezdařilo.";
                return;
            }

            var fileId = response.Data ?? Detail.Id;
            await LoadCoreAsync(fileId, ct).ConfigureAwait(false);
            StatusMessage = "Název byl aktualizován.";
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
                StatusMessage = "Aktualizace metadat se nezdařila.";
                return;
            }

            var fileId = response.Data ?? Detail.Id;
            await LoadCoreAsync(fileId, ct).ConfigureAwait(false);
            StatusMessage = "Metadata byla aktualizována.";
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
                StatusMessage = "Změna režimu se nezdařila.";
                return;
            }

            var fileId = response.Data ?? Detail.Id;
            await LoadCoreAsync(fileId, ct).ConfigureAwait(false);
            StatusMessage = isReadOnly
                ? "Soubor je nyní jen pro čtení."
                : "Soubor lze znovu upravovat.";
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
                StatusMessage = "Uložení platnosti se nezdařilo.";
                return;
            }

            var fileId = response.Data ?? Detail.Id;
            await LoadCoreAsync(fileId, ct).ConfigureAwait(false);
            StatusMessage = "Platnost byla aktualizována.";
        }, "Ukládám platnost dokumentu…");
    }

    [RelayCommand]
    private async Task ClearValidityAsync()
    {
        if (Detail is null)
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
                StatusMessage = "Odstranění platnosti se nezdařilo.";
                return;
            }

            await LoadCoreAsync(Detail.Id, ct).ConfigureAwait(false);
            StatusMessage = "Platnost byla odstraněna.";
        }, "Odebírám platnost dokumentu…");
    }

    private async Task LoadCoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _queryService.GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
        Detail = detail;

        if (detail is null)
        {
            HasError = true;
            StatusMessage = "Dokument nebyl nalezen.";
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

        StatusMessage = null;
    }
}
