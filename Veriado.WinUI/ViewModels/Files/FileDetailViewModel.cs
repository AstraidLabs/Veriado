using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.Contracts.Files;
using Veriado.Services.Files;

namespace Veriado.WinUI.ViewModels.Files;

public partial class FileDetailViewModel : ObservableObject
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
    private bool isReadOnly;

    [ObservableProperty]
    private bool canEdit = true;

    [ObservableProperty]
    private string? newName;

    [ObservableProperty]
    private string? author;

    [ObservableProperty]
    private string? mime;

    [ObservableProperty]
    private string? statusMessage;

    public FileDetailViewModel(IFileQueryService queryService, IFileOperationsService operations)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    [RelayCommand]
    public async Task Load(Guid id)
    {
        var detail = await _queryService.GetDetailAsync(id, CancellationToken.None).ConfigureAwait(false);
        Detail = detail;

        if (detail is null)
        {
            StatusMessage = "Dokument nebyl nalezen.";
            return;
        }

        NewName = detail.Name;
        Author = detail.Author;
        Mime = detail.Mime;
        IsReadOnly = detail.IsReadOnly;
        CanEdit = true;
        StatusMessage = null;

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
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (Detail is null || string.IsNullOrWhiteSpace(NewName))
        {
            return;
        }

        var response = await _operations
            .RenameAsync(Detail.Id, NewName.Trim(), CancellationToken.None)
            .ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            StatusMessage = "Přejmenování se nezdařilo.";
            return;
        }

        var fileId = response.Data ?? Detail.Id;
        await Load(fileId).ConfigureAwait(false);
        StatusMessage = "Název byl aktualizován.";
    }

    [RelayCommand]
    private async Task UpdateMetadataAsync()
    {
        if (Detail is null)
        {
            return;
        }

        var request = new UpdateMetadataRequest
        {
            FileId = Detail.Id,
            Author = string.IsNullOrWhiteSpace(Author) ? null : Author,
            Mime = string.IsNullOrWhiteSpace(Mime) ? null : Mime,
        };

        var response = await _operations.UpdateMetadataAsync(request, CancellationToken.None).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            StatusMessage = "Aktualizace metadat se nezdařila.";
            return;
        }

        var fileId = response.Data ?? Detail.Id;
        await Load(fileId).ConfigureAwait(false);
        StatusMessage = "Metadata byla aktualizována.";
    }

    [RelayCommand]
    private async Task SetReadOnlyAsync()
    {
        if (Detail is null)
        {
            return;
        }

        var response = await _operations
            .SetReadOnlyAsync(Detail.Id, IsReadOnly, CancellationToken.None)
            .ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            StatusMessage = "Změna režimu jen pro čtení se nezdařila.";
            return;
        }

        var fileId = response.Data ?? Detail.Id;
        await Load(fileId).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ApplyValidityAsync()
    {
        if (Detail is null)
        {
            return;
        }

        var issuedAt = ValidityIssuedAt ?? DateTimeOffset.UtcNow;
        var validUntil = ValidityValidUntil ?? issuedAt;
        var validity = new FileValidityDto(issuedAt, validUntil, ValidityHasPhysicalCopy, ValidityHasElectronicCopy);

        var response = await _operations
            .SetValidityAsync(Detail.Id, validity, CancellationToken.None)
            .ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            StatusMessage = "Uložení platnosti se nezdařilo.";
            return;
        }

        var fileId = response.Data ?? Detail.Id;
        await Load(fileId).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ClearValidityAsync()
    {
        if (Detail is null)
        {
            return;
        }

        var response = await _operations
            .ClearValidityAsync(Detail.Id, CancellationToken.None)
            .ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            StatusMessage = "Odstranění platnosti se nezdařilo.";
            return;
        }

        var fileId = response.Data ?? Detail.Id;
        await Load(fileId).ConfigureAwait(false);
    }
}
