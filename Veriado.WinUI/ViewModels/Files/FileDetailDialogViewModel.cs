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
                NotifyErrorCollectionsChanged();
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

    public bool CanSave
    {
        get
        {
            if (IsBusy)
            {
                return false;
            }

            var scope = GetValidationScope();
            if (scope == FileValidationScope.None)
            {
                return false;
            }

            return !HasScopeErrors(scope);
        }
    }

    public bool CanClearValidity => File.ValidFrom is not null || File.ValidTo is not null;

    public IEnumerable<string> FileNameErrors => GetErrors(nameof(EditableFileDetailModel.FileName));

    public IEnumerable<string> MimeTypeErrors => GetErrors(nameof(EditableFileDetailModel.MimeType));

    public IEnumerable<string> AuthorErrors => GetErrors(nameof(EditableFileDetailModel.Author));

    public IEnumerable<string> ValidFromErrors => GetErrors(nameof(EditableFileDetailModel.ValidFrom));

    public IEnumerable<string> ValidToErrors => GetErrors(nameof(EditableFileDetailModel.ValidTo));

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
        var scope = GetValidationScope();
        var validation = File.Validate(scope);
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(CanSave));
        NotifyErrorCollectionsChanged();
        SaveCommand.NotifyCanExecuteChanged();

        if (!validation.IsValid)
        {
            return;
        }

        if (scope == FileValidationScope.None)
        {
            return;
        }

        try
        {
            IsSaving = true;
            ErrorMessage = null;
            var request = BuildUpdateRequestFromScope(scope);
            var updated = await _fileService.UpdateAsync(request, cancellationToken).ConfigureAwait(false);
            var normalized = NormalizeDetail(updated);
            _snapshot = normalized;
            File.UpdateSnapshot(normalized);
            CloseRequested?.Invoke(this, new DialogResult(DialogOutcome.Primary));
        }
        catch (FileDetailValidationException ex)
        {
            File.ApplyServerErrors(ex.Errors);
            OnPropertyChanged(nameof(HasErrors));
            OnPropertyChanged(nameof(CanSave));
            NotifyErrorCollectionsChanged();
            SaveCommand.NotifyCanExecuteChanged();
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
        var normalized = NormalizeDetail(detail);
        _snapshot = normalized;
        File = EditableFileDetailModel.FromDto(normalized);
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
        NotifyErrorCollectionsChanged(e.PropertyName);
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void ExecuteClearValidity()
    {
        File.ValidFrom = null;
        File.ValidTo = null;
        File.Validate(FileValidationScope.Validity);
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(CanSave));
        NotifyErrorCollectionsChanged(nameof(EditableFileDetailModel.ValidFrom));
        NotifyErrorCollectionsChanged(nameof(EditableFileDetailModel.ValidTo));
        OnPropertyChanged(nameof(CanClearValidity));
        SaveCommand.NotifyCanExecuteChanged();
        ClearValidityCommand.NotifyCanExecuteChanged();
    }

    private bool CanExecuteClearValidity() => CanClearValidity;

    private FileValidationScope GetValidationScope()
    {
        if (_snapshot is null)
        {
            return FileValidationScope.None;
        }

        var scope = FileValidationScope.None;

        if (!string.Equals(File.FileName, _snapshot.FileName, StringComparison.Ordinal))
        {
            scope |= FileValidationScope.Metadata;
        }

        if (!string.Equals(File.MimeType, _snapshot.MimeType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(File.Author ?? string.Empty, _snapshot.Author ?? string.Empty, StringComparison.Ordinal)
            || File.IsReadOnly != _snapshot.IsReadOnly)
        {
            scope |= FileValidationScope.Metadata;
        }

        if (!Nullable.Equals(File.ValidFrom, _snapshot.ValidFrom)
            || !Nullable.Equals(File.ValidTo, _snapshot.ValidTo))
        {
            scope |= FileValidationScope.Validity;
        }

        return scope;
    }

    private bool HasScopeErrors(FileValidationScope scope)
    {
        if (scope == FileValidationScope.None)
        {
            return false;
        }

        if (scope.HasFlag(FileValidationScope.Metadata)
            && (HasErrorsForProperty(nameof(EditableFileDetailModel.FileName))
                || HasErrorsForProperty(nameof(EditableFileDetailModel.MimeType))
                || HasErrorsForProperty(nameof(EditableFileDetailModel.Author))))
        {
            return true;
        }

        if (scope.HasFlag(FileValidationScope.Validity)
            && (HasErrorsForProperty(nameof(EditableFileDetailModel.ValidFrom))
                || HasErrorsForProperty(nameof(EditableFileDetailModel.ValidTo))))
        {
            return true;
        }

        return false;
    }

    private bool HasErrorsForProperty(string propertyName)
    {
        return File.GetErrors(propertyName)?.Any() ?? false;
    }

    private EditableFileDetailDto BuildUpdateRequestFromScope(FileValidationScope scope)
    {
        if (_snapshot is null)
        {
            throw new InvalidOperationException("File detail has not been loaded.");
        }

        var original = _snapshot;
        var current = File;

        return new EditableFileDetailDto
        {
            Id = current.Id,
            FileName = scope.HasFlag(FileValidationScope.Metadata) ? current.FileName : original.FileName,
            Extension = original.Extension,
            MimeType = scope.HasFlag(FileValidationScope.Metadata) ? current.MimeType : original.MimeType,
            Author = scope.HasFlag(FileValidationScope.Metadata)
                ? EditableFileDetailModel.NormalizeAuthor(current.Author)
                : EditableFileDetailModel.NormalizeAuthor(original.Author),
            IsReadOnly = scope.HasFlag(FileValidationScope.Metadata) ? current.IsReadOnly : original.IsReadOnly,
            Size = original.Size,
            CreatedAt = original.CreatedAt,
            ModifiedAt = original.ModifiedAt,
            Version = original.Version,
            ValidFrom = scope.HasFlag(FileValidationScope.Validity) ? current.ValidFrom : original.ValidFrom,
            ValidTo = scope.HasFlag(FileValidationScope.Validity) ? current.ValidTo : original.ValidTo,
        };
    }

    private static EditableFileDetailDto NormalizeDetail(EditableFileDetailDto detail)
    {
        return new EditableFileDetailDto
        {
            Id = detail.Id,
            FileName = detail.FileName,
            Extension = detail.Extension,
            MimeType = detail.MimeType,
            Author = EditableFileDetailModel.NormalizeAuthor(detail.Author),
            IsReadOnly = detail.IsReadOnly,
            Size = detail.Size,
            CreatedAt = detail.CreatedAt,
            ModifiedAt = detail.ModifiedAt,
            Version = detail.Version,
            ValidFrom = detail.ValidFrom,
            ValidTo = detail.ValidTo,
        };
    }

    private void OnBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanEditFields));
        OnPropertyChanged(nameof(CanSave));
        CancelCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void NotifyErrorCollectionsChanged(string? propertyName = null)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            OnPropertyChanged(nameof(FileNameErrors));
            OnPropertyChanged(nameof(MimeTypeErrors));
            OnPropertyChanged(nameof(AuthorErrors));
            OnPropertyChanged(nameof(ValidFromErrors));
            OnPropertyChanged(nameof(ValidToErrors));
            return;
        }

        if (string.Equals(propertyName, nameof(EditableFileDetailModel.FileName), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(FileNameErrors));
        }
        else if (string.Equals(propertyName, nameof(EditableFileDetailModel.MimeType), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(MimeTypeErrors));
        }
        else if (string.Equals(propertyName, nameof(EditableFileDetailModel.Author), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(AuthorErrors));
        }
        else if (string.Equals(propertyName, nameof(EditableFileDetailModel.ValidFrom), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ValidFromErrors));
        }
        else if (string.Equals(propertyName, nameof(EditableFileDetailModel.ValidTo), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ValidToErrors));
        }
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
