using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.Application.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Files;

namespace Veriado.WinUI.ViewModels;

/// <summary>
/// Provides the presentation logic for the file detail page.
/// </summary>
public sealed partial class FileDetailViewModel : ObservableObject
{
    private readonly IFileQueryService _fileQueryService;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly IFileContentService _fileContentService;

    public FileDetailViewModel(
        IFileQueryService fileQueryService,
        IFileOperationsService fileOperationsService,
        IFileContentService fileContentService)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _fileOperationsService = fileOperationsService ?? throw new ArgumentNullException(nameof(fileOperationsService));
        _fileContentService = fileContentService ?? throw new ArgumentNullException(nameof(fileContentService));
    }

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isInfoBarOpen = true;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private Guid? currentFileId;

    [ObservableProperty]
    private FileDetailDto? detail;

    [ObservableProperty]
    private string editableName = string.Empty;

    [ObservableProperty]
    private string editableAuthor = string.Empty;

    [ObservableProperty]
    private string editableMime = string.Empty;

    [ObservableProperty]
    private bool editableIsReadOnly;

    [ObservableProperty]
    private DateTimeOffset? validityIssuedAt;

    [ObservableProperty]
    private DateTimeOffset? validityValidUntil;

    [ObservableProperty]
    private bool validityHasPhysicalCopy;

    [ObservableProperty]
    private bool validityHasElectronicCopy;

    [ObservableProperty]
    private string? contentPreview;

    [ObservableProperty]
    private bool extractContent = true;

    /// <summary>
    /// Loads the detail information for the specified file.
    /// </summary>
    public async Task GetDetailAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "Načítám detail souboru...";
        IsInfoBarOpen = true;

        try
        {
            var detail = await _fileQueryService.GetDetailAsync(fileId, cancellationToken).ConfigureAwait(true);
            if (detail is null)
            {
                StatusMessage = "Soubor nebyl nalezen.";
                CurrentFileId = null;
                Detail = null;
                return;
            }

            PopulateFromDetail(detail);
            StatusMessage = "Detail načten.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Načítání bylo zrušeno.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Načítání selhalo: {ex.Message}";
            StatusMessage = "Načítání selhalo.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Persists the edited metadata using the orchestration services.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task UpdateMetadataAsync(CancellationToken cancellationToken)
    {
        if (CurrentFileId is null)
        {
            StatusMessage = "Vyberte soubor.";
            return;
        }

        var request = new UpdateMetadataRequest
        {
            FileId = CurrentFileId.Value,
            Author = EditableAuthor,
            Mime = string.IsNullOrWhiteSpace(EditableMime) ? null : EditableMime.Trim(),
            IsReadOnly = EditableIsReadOnly,
        };

        await ExecuteAsync(
            () => _fileOperationsService.UpdateMetadataAsync(request, cancellationToken),
            "Metadata byla uložena.");
    }

    /// <summary>
    /// Renames the file aggregate.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RenameAsync(CancellationToken cancellationToken)
    {
        if (CurrentFileId is null || string.IsNullOrWhiteSpace(EditableName))
        {
            StatusMessage = "Zadejte nový název.";
            return;
        }

        await ExecuteAsync(
            () => _fileOperationsService.RenameAsync(CurrentFileId.Value, EditableName.Trim(), cancellationToken),
            "Soubor byl přejmenován.");
    }

    /// <summary>
    /// Applies only the read-only toggle using the specialized command.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SetReadOnlyAsync(CancellationToken cancellationToken)
    {
        if (CurrentFileId is null)
        {
            return;
        }

        await ExecuteAsync(
            () => _fileOperationsService.SetReadOnlyAsync(CurrentFileId.Value, EditableIsReadOnly, cancellationToken),
            EditableIsReadOnly ? "Soubor je nyní pouze pro čtení." : "Soubor lze upravovat.");
    }

    /// <summary>
    /// Applies the validity parameters captured by the view.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ApplyValidityAsync(CancellationToken cancellationToken)
    {
        if (CurrentFileId is null)
        {
            return;
        }

        if (ValidityIssuedAt is null || ValidityValidUntil is null)
        {
            StatusMessage = "Zadejte platná data platnosti.";
            return;
        }

        var validity = new FileValidityDto(
            ValidityIssuedAt.Value,
            ValidityValidUntil.Value,
            ValidityHasPhysicalCopy,
            ValidityHasElectronicCopy);

        await ExecuteAsync(
            () => _fileOperationsService.SetValidityAsync(CurrentFileId.Value, validity, cancellationToken),
            "Platnost byla uložena.");
    }

    /// <summary>
    /// Clears the validity metadata on the file.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ClearValidityAsync(CancellationToken cancellationToken)
    {
        if (CurrentFileId is null)
        {
            return;
        }

        await ExecuteAsync(
            () => _fileOperationsService.ClearValidityAsync(CurrentFileId.Value, cancellationToken),
            "Platnost byla odstraněna.");
    }

    /// <summary>
    /// Retrieves file content from storage and populates the preview.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task LoadContentPreviewAsync(CancellationToken cancellationToken)
    {
        if (CurrentFileId is null)
        {
            return;
        }

        try
        {
            StatusMessage = "Načítám obsah...";
            IsInfoBarOpen = true;

            var content = await _fileContentService.GetContentAsync(CurrentFileId.Value, cancellationToken).ConfigureAwait(true);
            if (content is null)
            {
                ContentPreview = null;
                StatusMessage = "Obsah nebyl nalezen.";
                return;
            }

            var (meta, bytes) = content.Value;
            var previewLength = Math.Min(bytes.Length, 512);
            ContentPreview = Encoding.UTF8.GetString(bytes, 0, previewLength);
            StatusMessage = $"Načten obsah (velikost {meta.SizeBytes:N0} B).";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Načítání obsahu bylo zrušeno.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Načítání obsahu selhalo: {ex.Message}";
            StatusMessage = "Načítání obsahu selhalo.";
        }
    }

    /// <summary>
    /// Replaces the file content using the orchestration service.
    /// </summary>
    public async Task ReplaceContentAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        if (CurrentFileId is null)
        {
            return;
        }

        if (content is null || content.Length == 0)
        {
            StatusMessage = "Nebyl vybrán platný soubor.";
            return;
        }

        await ExecuteAsync(
            () => _fileOperationsService.ReplaceContentAsync(CurrentFileId.Value, content, ExtractContent, cancellationToken),
            "Obsah byl nahrán.");
    }

    private async Task ExecuteAsync(Func<Task<AppResult<Guid>>> callback, string successMessage)
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var result = await callback().ConfigureAwait(true);
            if (result.IsSuccess)
            {
                StatusMessage = successMessage;
                IsInfoBarOpen = true;
                if (CurrentFileId is { } id)
                {
                    await GetDetailAsync(id).ConfigureAwait(true);
                }
            }
            else
            {
                HandleFailure(result);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void HandleFailure(AppResult<Guid> result)
    {
        IsInfoBarOpen = true;

        var message = string.IsNullOrWhiteSpace(result.Error.Message)
            ? result.Error.Code.ToString()
            : result.Error.Message;

        ErrorMessage = $"Operace selhala: {message}";
        StatusMessage = "Operace selhala.";
    }

    private void PopulateFromDetail(FileDetailDto detail)
    {
        CurrentFileId = detail.Id;
        Detail = detail;
        EditableName = detail.Name;
        EditableAuthor = detail.Author;
        EditableMime = detail.Mime;
        EditableIsReadOnly = detail.IsReadOnly;
        ValidityIssuedAt = detail.Validity?.IssuedAt;
        ValidityValidUntil = detail.Validity?.ValidUntil;
        ValidityHasPhysicalCopy = detail.Validity?.HasPhysicalCopy ?? false;
        ValidityHasElectronicCopy = detail.Validity?.HasElectronicCopy ?? false;
        ContentPreview = null;
    }
}
