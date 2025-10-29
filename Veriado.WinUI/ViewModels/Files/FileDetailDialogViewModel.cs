using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.Appl.Files;
using Veriado.Appl.Files.Contracts;
using Veriado.Services.Files.Exceptions;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;


namespace Veriado.WinUI.ViewModels.Files;

/// <summary>
/// ViewModel for the file detail dialog that coordinates loading, validation and persistence.
/// </summary>
public sealed partial class FileDetailDialogViewModel : ObservableObject, IDialogAware
{
    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;
    private EditableFileDetailModel _file = CreatePlaceholderModel();
    private EditableFileDetailDto? _snapshot;
    private CancellationTokenSource? _saveCancellation;
    private bool _isLoading;
    private bool _isSaving;
    private string? _errorMessage;

    public FileDetailDialogViewModel(IFileService fileService, IDialogService dialogService)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        SaveCommand = new AsyncRelayCommand(ExecuteSaveAsync, CanExecuteSave);
        CancelCommand = new RelayCommand(ExecuteCancel, () => !IsSaving);
        ClearValidityCommand = new RelayCommand(ExecuteClearValidity, CanExecuteClearValidity);

        File = CreatePlaceholderModel();
    }

    public event EventHandler<DialogResult>? CloseRequested;

    public EditableFileDetailModel File
    {
        get => _file;
        private set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_file, value))
            {
                return;
            }

            if (_file is not null)
            {
                _file.PropertyChanged -= OnFilePropertyChanged;
                _file.ErrorsChanged -= OnFileErrorsChanged;
            }

            if (SetProperty(ref _file, value))
            {
                value.PropertyChanged += OnFilePropertyChanged;
                value.ErrorsChanged += OnFileErrorsChanged;

                OnPropertyChanged(nameof(HasErrors));
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(CanClearValidity));
                SaveCommand.NotifyCanExecuteChanged();
                ClearValidityCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnBusyStateChanged();
            }
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                OnBusyStateChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasErrorMessage));
            }
        }
    }

    public bool HasErrors => File.HasErrors;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsBusy => IsSaving || IsLoading;

    public bool CanEditFields => !IsBusy;

    public bool CanSave => !IsBusy && !HasErrors;

    public bool CanClearValidity => File.ValidFrom is not null || File.ValidTo is not null;

    public IAsyncRelayCommand SaveCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ClearValidityCommand { get; }

    public IEnumerable<string> GetErrors(string propertyName)
    {
        return File
            .GetErrors(propertyName)
            ?.Cast<object?>()
            .Select(static error => error?.ToString() ?? string.Empty)
            ?? Array.Empty<string>();
    }

    public async Task LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            IsLoading = true;
            var detail = await _fileService.GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
            Attach(detail);
            ErrorMessage = null;
        }
        catch (FileDetailNotFoundException ex)
        {
            await _dialogService.ShowErrorAsync("Soubor nenalezen", ex.Message).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _dialogService.ShowErrorAsync("Chyba načítání", ex.Message).ConfigureAwait(false);
            throw;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        File.ResetValidation();
        File.ValidateAll();
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(CanSave));

        if (File.HasErrors)
        {
            return;
        }

        try
        {
            IsSaving = true;
            ErrorMessage = null;
            var dto = File.ToDto();
            await _fileService.UpdateAsync(dto, cancellationToken).ConfigureAwait(false);
            _snapshot = dto;
            File.UpdateSnapshot(dto);
            CloseRequested?.Invoke(this, new DialogResult(DialogOutcome.Primary));
        }
        catch (FileDetailValidationException ex)
        {
            File.ApplyServerErrors(ex.Errors);
            OnPropertyChanged(nameof(HasErrors));
            OnPropertyChanged(nameof(CanSave));
            ErrorMessage = ex.Message;
        }
        catch (FileDetailConcurrencyException ex)
        {
            ErrorMessage = ex.Message;
            await HandleConcurrencyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (FileDetailServiceException ex)
        {
            ErrorMessage = ex.Message;
            await _dialogService.ShowErrorAsync("Uložení selhalo", ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
            await _dialogService.ShowErrorAsync("Uložení selhalo", ex.Message).ConfigureAwait(false);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanExecuteSave() => CanSave;

    private async Task ExecuteSaveAsync()
    {
        if (IsSaving)
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        _saveCancellation = cts;
        try
        {
            await SaveAsync(cts.Token).ConfigureAwait(false);
        }
        finally
        {
            _saveCancellation = null;
        }
    }

    private void ExecuteCancel()
    {
        if (IsSaving)
        {
            _saveCancellation?.Cancel();
            return;
        }

        CloseRequested?.Invoke(this, new DialogResult(DialogOutcome.Close));
    }

    private void Attach(EditableFileDetailDto detail)
    {
        _snapshot = detail;
        File = EditableFileDetailModel.FromDto(detail);
    }

    private async Task HandleConcurrencyAsync(CancellationToken cancellationToken)
    {
        if (_snapshot is null)
        {
            return;
        }

        var textBlock = new TextBlock
        {
            Text = "Dokument byl mezitím upraven jiným uživatelem. Chcete znovu načíst aktuální data?",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        };

        var request = new DialogRequest(
            "Konflikt při ukládání",
            textBlock,
            "Znovu načíst",
            SecondaryButtonText: "Zavřít",
            DefaultButton: ContentDialogButton.Primary);

        var result = await _dialogService.ShowDialogAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.IsPrimary)
        {
            await LoadAsync(_snapshot.Id, cancellationToken).ConfigureAwait(false);
        }
        else if (result.IsSecondary)
        {
            CloseRequested?.Invoke(this, new DialogResult(DialogOutcome.Close));
        }
    }

    private void OnFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditableFileDetailModel.HasValidity))
        {
            OnPropertyChanged(nameof(HasErrors));
        }

        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanClearValidity));
        SaveCommand.NotifyCanExecuteChanged();
        ClearValidityCommand.NotifyCanExecuteChanged();
    }

    private void OnFileErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void ExecuteClearValidity()
    {
        File.ValidFrom = null;
        File.ValidTo = null;
        File.ValidateAll();
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanClearValidity));
        SaveCommand.NotifyCanExecuteChanged();
        ClearValidityCommand.NotifyCanExecuteChanged();
    }

    private bool CanExecuteClearValidity() => CanClearValidity;

    private void OnBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanEditFields));
        OnPropertyChanged(nameof(CanSave));
        CancelCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }
    private static EditableFileDetailModel CreatePlaceholderModel()
    {
        return EditableFileDetailModel.FromDto(new EditableFileDetailDto
        {
            Id = Guid.Empty,
            FileName = string.Empty,
            Extension = string.Empty,
            MimeType = string.Empty,
            CreatedAt = DateTimeOffset.Now,
            ModifiedAt = DateTimeOffset.Now,
            Version = 0,
            Size = 0,
        });
    }
}
