// BEGIN CHANGE Veriado.WinUI/ViewModels/DetailViewModel.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Application.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Files;
using Veriado.WinUI.Messages;

namespace Veriado.WinUI.ViewModels;

/// <summary>
/// Provides state and commands for the file detail view.
/// </summary>
public sealed partial class DetailViewModel : BaseViewModel, IRecipient<SelectedFileChangedMessage>
{
    private readonly IFileQueryService _fileQueryService;
    private readonly IFileOperationsService _fileOperationsService;

    [ObservableProperty]
    private Guid? fileId;

    [ObservableProperty]
    private FileDetailDto? detail;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string extension = string.Empty;

    [ObservableProperty]
    private string mime = string.Empty;

    [ObservableProperty]
    private string author = string.Empty;

    [ObservableProperty]
    private bool isReadOnly;

    [ObservableProperty]
    private DateTimeOffset? validityIssuedAt;

    [ObservableProperty]
    private DateTimeOffset? validityValidUntil;

    [ObservableProperty]
    private bool hasPhysicalCopy;

    [ObservableProperty]
    private bool hasElectronicCopy;

    public DetailViewModel(
        IFileQueryService fileQueryService,
        IFileOperationsService fileOperationsService,
        IMessenger messenger)
        : base(messenger)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _fileOperationsService = fileOperationsService ?? throw new ArgumentNullException(nameof(fileOperationsService));
    }

    public bool HasDetail => Detail is not null;

    [RelayCommand(IncludeCancelCommand = true, AllowConcurrentExecutions = false)]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (FileId is null)
        {
            Detail = null;
            StatusMessage = "Nebyl vybrán žádný soubor.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Načítám detail souboru...";

        try
        {
            var detail = await _fileQueryService.GetDetailAsync(FileId.Value, cancellationToken).ConfigureAwait(false);
            if (detail is null)
            {
                StatusMessage = "Soubor nebyl nalezen.";
                Detail = null;
                return;
            }

            Detail = detail;
            Name = detail.Name;
            Extension = detail.Extension;
            Mime = detail.Mime;
            Author = detail.Author;
            IsReadOnly = detail.IsReadOnly;
            ValidityIssuedAt = detail.Validity?.IssuedAt;
            ValidityValidUntil = detail.Validity?.ValidUntil;
            HasPhysicalCopy = detail.Validity?.HasPhysicalCopy ?? false;
            HasElectronicCopy = detail.Validity?.HasElectronicCopy ?? false;
            StatusMessage = "Detail načten.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Načítání detailu zrušeno.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Načítání detailu selhalo: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasDetail));
        }
    }

    [RelayCommand(IncludeCancelCommand = true, AllowConcurrentExecutions = false)]
    private async Task SaveMetadataAsync(CancellationToken cancellationToken)
    {
        if (FileId is null)
        {
            StatusMessage = "Vyberte soubor.";
            return;
        }

        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Ukládám metadata...";

        try
        {
            var request = new UpdateMetadataRequest
            {
                FileId = FileId.Value,
                Author = Author,
                Mime = string.IsNullOrWhiteSpace(Mime) ? null : Mime,
                IsReadOnly = IsReadOnly,
            };

            var result = await _fileOperationsService.UpdateMetadataAsync(request, cancellationToken).ConfigureAwait(false);
            HandleResult(result, "Metadata byla uložena.");
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync(cancellationToken).ConfigureAwait(false);
        Messenger.Send(new GridRefreshMessage(new GridRefreshRequest(false)));
    }

    [RelayCommand(IncludeCancelCommand = true, AllowConcurrentExecutions = false)]
    private async Task RenameAsync(CancellationToken cancellationToken)
    {
        if (FileId is null || string.IsNullOrWhiteSpace(Name))
        {
            StatusMessage = "Zadejte nový název.";
            return;
        }

        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Přejmenovávám soubor...";

        try
        {
            var result = await _fileOperationsService
                .RenameAsync(FileId.Value, Name.Trim(), cancellationToken)
                .ConfigureAwait(false);
            HandleResult(result, "Soubor byl přejmenován.");
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync(cancellationToken).ConfigureAwait(false);
        Messenger.Send(new GridRefreshMessage(new GridRefreshRequest(true)));
    }

    [RelayCommand(IncludeCancelCommand = true, AllowConcurrentExecutions = false)]
    private async Task ApplyValidityAsync(CancellationToken cancellationToken)
    {
        if (FileId is null)
        {
            StatusMessage = "Vyberte soubor.";
            return;
        }

        if (ValidityIssuedAt is null || ValidityValidUntil is null)
        {
            StatusMessage = "Zadejte platné datumy platnosti.";
            return;
        }

        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Aktualizuji platnost dokumentu...";

        try
        {
            var dto = new FileValidityDto(
                ValidityIssuedAt.Value,
                ValidityValidUntil.Value,
                HasPhysicalCopy,
                HasElectronicCopy);

            var result = await _fileOperationsService
                .SetValidityAsync(FileId.Value, dto, cancellationToken)
                .ConfigureAwait(false);
            HandleResult(result, "Platnost dokumentu byla aktualizována.");
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync(cancellationToken).ConfigureAwait(false);
        Messenger.Send(new GridRefreshMessage(new GridRefreshRequest(false)));
    }

    [RelayCommand(IncludeCancelCommand = true, AllowConcurrentExecutions = false)]
    private async Task ClearValidityAsync(CancellationToken cancellationToken)
    {
        if (FileId is null)
        {
            return;
        }

        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Odstraňuji platnost dokumentu...";

        try
        {
            var result = await _fileOperationsService
                .ClearValidityAsync(FileId.Value, cancellationToken)
                .ConfigureAwait(false);
            HandleResult(result, "Platnost dokumentu byla odstraněna.");
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync(cancellationToken).ConfigureAwait(false);
        Messenger.Send(new GridRefreshMessage(new GridRefreshRequest(false)));
    }

    public void Receive(SelectedFileChangedMessage message)
    {
        if (FileId == message.Value)
        {
            return;
        }

        FileId = message.Value;
        _ = LoadCommand.ExecuteAsync(null);
    }

    partial void OnDetailChanged(FileDetailDto? value)
    {
        OnPropertyChanged(nameof(HasDetail));
    }

    private void HandleResult(AppResult<Guid> result, string successMessage)
    {
        if (result.IsSuccess)
        {
            StatusMessage = successMessage;
            return;
        }

        var error = result.Error;
        var message = string.IsNullOrWhiteSpace(error.Message)
            ? error.Code.ToString()
            : error.Message;
        StatusMessage = $"Operace selhala: {message}";
    }
}
// END CHANGE Veriado.WinUI/ViewModels/DetailViewModel.cs
