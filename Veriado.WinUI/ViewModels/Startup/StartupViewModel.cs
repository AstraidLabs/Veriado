using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Veriado.WinUI.ViewModels.Startup;

public partial class StartupViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? detailsMessage;

    public event EventHandler? RetryRequested;

    public void ShowProgress(string message)
    {
        StatusMessage = message;
        DetailsMessage = null;
        HasError = false;
        IsLoading = true;
    }

    public void ShowError(string message, string? details)
    {
        StatusMessage = message;
        DetailsMessage = details;
        IsLoading = false;
        HasError = true;
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry()
    {
        RetryRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanRetry() => HasError;

    partial void OnHasErrorChanged(bool value)
    {
        RetryCommand.NotifyCanExecuteChanged();
    }
}
