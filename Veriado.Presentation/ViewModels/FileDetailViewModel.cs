using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Files;
using Veriado.Presentation.Services;

namespace Veriado.Presentation.ViewModels;

/// <summary>
/// Provides the presentation logic for the file detail page.
/// </summary>
public sealed partial class FileDetailViewModel : ViewModelBase
{
    private readonly IFileQueryService _fileQueryService;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly IFileContentService _fileContentService;
    private readonly IPickerService _pickerService;
    private readonly IDialogService _dialogService;

    public FileDetailViewModel(
        IFileQueryService fileQueryService,
        IFileOperationsService fileOperationsService,
        IFileContentService fileContentService,
        IPickerService pickerService,
        IDialogService dialogService,
        IMessenger messenger)
        : base(messenger)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _fileOperationsService = fileOperationsService ?? throw new ArgumentNullException(nameof(fileOperationsService));
        _fileContentService = fileContentService ?? throw new ArgumentNullException(nameof(fileContentService));
        _pickerService = pickerService ?? throw new ArgumentNullException(nameof(pickerService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    }

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
    [RelayCommand(IncludeCancelCommand = true)]
    private Task LoadAsync(Guid fileId, CancellationToken cancellationToken)
        => LoadDetailAsync(fileId, cancellationToken);

    /// <summary>
    /// Persists the edited metadata using the orchestration services.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private Task UpdateMetadataAsync(CancellationToken cancellationToken)
    {
        if (CurrentFileId is null)
        {
            StatusMessage = "Vyberte soubor.";
            return Task.CompletedTask;
        }

        var request = new UpdateMetadataRequest
        {
            FileId = CurrentFileId.Value,
            Author = string.IsNullOrWhiteSpace(EditableAuthor) ? null : EditableAuthor.Trim(),
            Mime = string.IsNullOrWhiteSpace(EditableMime) ? null : EditableMime.Trim(),
            IsReadOnly = EditableIsReadOnly,
        };

        return ExecuteOperationAsync(
            token => _fileOperationsService.UpdateMetadataAsync(request, token),
            "Ukládám metadata...",
            "Metadata byla uložena.",
            cancellationToken);
    }

    /// <summary>
    /// Renames the file aggregate.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private Task RenameAsync(CancellationToken cancellationToken)
    {
        if (CurrentFileId is null || string.IsNullOrWhiteSpace(EditableName))
        {
            StatusMessage = "Zadejte nový název.";
            return Task.CompletedTask;
        }

        return ExecuteOperationAsync(
            token => _fileOperationsService.RenameAsync(CurrentFileId.Value, EditableName.Trim(), token),
            "Přejmenovávám soubor...",
            "Soubor byl přejmenován.",
            cancellationToken);
    }

    /// <summary>
    /// Applies only the read-only toggle using the specialized command.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private Task SetReadOnlyAsync(CancellationToken cancellationToken)
    {
        if (CurrentFileId is null)
        {
            return Task.CompletedTask;
        }

        return ExecuteOperationAsync(
            token => _fileOperationsService.SetReadOnlyAsync(CurrentFileId.Value, EditableIsReadOnly, token),
            EditableIsReadOnly ? "Nastavuji pouze pro čtení..." : "Povoluji úpravy...",
            EditableIsReadOnly ? "Soubor je nyní pouze pro čtení." : "Soubor lze upravovat.",
            cancellationToken);
    }

    /// <summary>
    /// Applies the validity parameters captured by the view.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private Task ApplyValidityAsync(CancellationToken cancellationToken)
    {
        if (CurrentFileId is null)
        {
            return Task.CompletedTask;
        }

        if (ValidityIssuedAt is null || ValidityValidUntil is null)
        {
            StatusMessage = "Zadejte platná data platnosti.";
            return Task.CompletedTask;
        }

        var validity = new FileValidityDto(
            ValidityIssuedAt.Value,
            ValidityValidUntil.Value,
            ValidityHasPhysicalCopy,
            ValidityHasElectronicCopy);

        return ExecuteOperationAsync(
            token => _fileOperationsService.SetValidityAsync(CurrentFileId.Value, validity, token),
            "Ukládám platnost...",
            "Platnost byla uložena.",
            cancellationToken);
    }

    /// <summary>
    /// Clears the validity metadata on the file.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private Task ClearValidityAsync(CancellationToken cancellationToken)
    {
        if (CurrentFileId is null)
        {
            return Task.CompletedTask;
        }

        return ExecuteOperationAsync(
            token => _fileOperationsService.ClearValidityAsync(CurrentFileId.Value, token),
            "Odebírám platnost...",
            "Platnost byla odstraněna.",
            cancellationToken);
    }

    /// <summary>
    /// Retrieves file content from storage and populates the preview.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private Task LoadContentPreviewAsync(CancellationToken cancellationToken)
    {
        if (CurrentFileId is null)
        {
            return Task.CompletedTask;
        }

        return SafeExecuteAsync(
            async token =>
            {
                StatusMessage = "Načítám obsah...";
                IsInfoBarOpen = true;

                var content = await _fileContentService.GetContentAsync(CurrentFileId.Value, token).ConfigureAwait(true);
                if (content is null)
                {
                    ContentPreview = null;
                    StatusMessage = "Obsah nebyl nalezen.";
                    return;
                }

                var bytes = content.Content;
                var previewLength = Math.Min(bytes.Length, 512);
                ContentPreview = Encoding.UTF8.GetString(bytes, 0, previewLength);
                StatusMessage = $"Načten obsah (velikost {content.SizeBytes:N0} B).";
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Replaces the file content using the orchestration service.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ReplaceContentAsync(CancellationToken cancellationToken)
    {
        if (CurrentFileId is null)
        {
            return;
        }

        var picked = await _pickerService.PickFileAsync(cancellationToken).ConfigureAwait(true);
        if (picked is null || picked.Content.Length == 0)
        {
            StatusMessage = "Nebyl vybrán platný soubor.";
            return;
        }

        var confirm = await _dialogService
            .ShowConfirmationAsync("Nahradit obsah", "Opravdu chcete nahradit obsah souboru?", cancellationToken)
            .ConfigureAwait(true);
        if (!confirm)
        {
            StatusMessage = "Nahrávání bylo zrušeno.";
            return;
        }

        await ExecuteOperationAsync(
            token => _fileOperationsService.ReplaceContentAsync(CurrentFileId.Value, picked.Content, ExtractContent, token),
            "Nahrávám obsah...",
            "Obsah byl nahrán.",
            cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadDetailAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (fileId == Guid.Empty)
        {
            return;
        }

        CurrentFileId = fileId;

        await SafeExecuteAsync(
            async token =>
            {
                var detail = await _fileQueryService.GetDetailAsync(fileId, token).ConfigureAwait(true);
                if (detail is null)
                {
                    StatusMessage = "Soubor nebyl nalezen.";
                    CurrentFileId = null;
                    Detail = null;
                    return;
                }

                PopulateFromDetail(detail);
                IsInfoBarOpen = true;
                StatusMessage = "Detail načten.";
            },
            "Načítám detail souboru...",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteOperationAsync(
        Func<CancellationToken, Task<ApiResponse<Guid>>> operation,
        string pendingMessage,
        string successMessage,
        CancellationToken cancellationToken)
    {
        await SafeExecuteAsync(
            async token =>
            {
                var response = await operation(token).ConfigureAwait(true);
                if (response.IsSuccess)
                {
                    StatusMessage = successMessage;
                    IsInfoBarOpen = true;
                    if (CurrentFileId is { } id)
                    {
                        await LoadDetailAsync(id, token).ConfigureAwait(true);
                    }
                }
                else
                {
                    HandleFailure(response);
                }
            },
            pendingMessage,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private void HandleFailure(ApiResponse<Guid> response)
    {
        IsInfoBarOpen = true;

        if (response.Errors.Count > 0)
        {
            var messages = response.Errors
                .Select(error => string.IsNullOrWhiteSpace(error.Message) ? error.Code : error.Message)
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToArray();

            LastError = messages.Length > 0
                ? string.Join(Environment.NewLine, messages)
                : "Operace selhala.";
        }
        else
        {
            LastError = "Operace selhala.";
        }

        StatusMessage = "Operace selhala.";
    }

    private void PopulateFromDetail(FileDetailDto detail)
    {
        Detail = detail;
        EditableName = detail.Name;
        EditableAuthor = detail.Author ?? string.Empty;
        EditableMime = detail.Mime ?? string.Empty;
        EditableIsReadOnly = detail.IsReadOnly;
        ValidityIssuedAt = detail.Validity?.IssuedAt;
        ValidityValidUntil = detail.Validity?.ValidUntil;
        ValidityHasPhysicalCopy = detail.Validity?.HasPhysicalCopy ?? false;
        ValidityHasElectronicCopy = detail.Validity?.HasElectronicCopy ?? false;
        ContentPreview = null;
    }
}
