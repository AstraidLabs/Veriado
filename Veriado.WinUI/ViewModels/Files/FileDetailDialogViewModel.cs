using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.Application.Files;
using Veriado.Application.Files.Contracts;
using Veriado.Services.Files.Exceptions;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;
using System.Threading;

namespace Veriado.WinUI.ViewModels.Files;

/// <summary>
/// ViewModel for the file detail dialog that coordinates loading, validation and persistence.
/// </summary>
public sealed partial class FileDetailDialogViewModel : ObservableObject, IDialogAware
{
    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;
    private EditableFileDetailModel? _file;
    private FileDetailDto? _snapshot;
    private CancellationTokenSource? _saveCancellation;

    public FileDetailDialogViewModel(IFileService fileService, IDialogService dialogService)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        SaveCommand = new AsyncRelayCommand(ExecuteSaveAsync, CanSave);
        CancelCommand = new RelayCommand(ExecuteCancel, () => !IsSaving);
        ClearValidityCommand = new RelayCommand(ExecuteClearValidity, CanClearValidity);
    }

    public event EventHandler<DialogResult>? CloseRequested;

    public EditableFileDetailModel? File
    {
        get => _file;
        private set
        {
            if (SetProperty(ref _file, value))
            {
                OnPropertyChanged(nameof(HasErrors));
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isSaving;

    [ObservableProperty]
    private string? errorMessage;

    public bool HasErrors => File?.HasErrors ?? false;

    public IAsyncRelayCommand SaveCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ClearValidityCommand { get; }

    public IEnumerable<string> GetErrors(string propertyName)
    {
        if (File is null)
        {
            return Array.Empty<string>();
        }

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
        if (File is null)
        {
            return;
        }

        File.ResetValidation();
        File.ValidateAll();
        OnPropertyChanged(nameof(HasErrors));

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
            CancelCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSave()
        => !IsLoading && !IsSaving && File is not null;

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

    private void Attach(FileDetailDto detail)
    {
        _snapshot = detail;
        if (File is { } current)
        {
            current.PropertyChanged -= OnFilePropertyChanged;
            current.ErrorsChanged -= OnFileErrorsChanged;
        }

        var model = EditableFileDetailModel.FromDto(detail);
        model.PropertyChanged += OnFilePropertyChanged;
        model.ErrorsChanged += OnFileErrorsChanged;
        File = model;
        ClearValidityCommand.NotifyCanExecuteChanged();
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
            secondaryButtonText: "Zavřít",
            defaultButton: ContentDialogButton.Primary);

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

        SaveCommand.NotifyCanExecuteChanged();
        ClearValidityCommand.NotifyCanExecuteChanged();
    }

    private void OnFileErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasErrors));
        SaveCommand.NotifyCanExecuteChanged();
        ClearValidityCommand.NotifyCanExecuteChanged();
    }

    private void ExecuteClearValidity()
    {
        if (File is null)
        {
            return;
        }

        File.ValidFrom = null;
        File.ValidTo = null;
        File.ValidateAll();
        OnPropertyChanged(nameof(HasErrors));
        SaveCommand.NotifyCanExecuteChanged();
        ClearValidityCommand.NotifyCanExecuteChanged();
    }

    private bool CanClearValidity()
        => File is { ValidFrom: not null } || File is { ValidTo: not null };
}
