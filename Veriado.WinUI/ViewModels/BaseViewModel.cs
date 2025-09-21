// BEGIN CHANGE Veriado.WinUI/ViewModels/BaseViewModel.cs
using System;
using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace Veriado.WinUI.ViewModels;

/// <summary>
/// Provides a common observable base type for view models with messaging and validation support.
/// </summary>
public abstract class BaseViewModel : ObservableRecipient, INotifyDataErrorInfo
{
    private readonly ObservableValidatorProxy _validator = new();
    private bool _isBusy;
    private string? _statusMessage;

    protected BaseViewModel(IMessenger messenger)
        : base(messenger)
    {
        _validator.ErrorsChanged += (sender, args) => ErrorsChanged?.Invoke(this, args);
        IsActive = true;
    }

    /// <summary>
    /// Gets a value indicating whether the view model is performing a long-running operation.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        protected set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnIsBusyChanged(value);
            }
        }
    }

    /// <summary>
    /// Gets the current status message presented to the shell.
    /// </summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        protected set => SetProperty(ref _statusMessage, value);
    }

    /// <inheritdoc />
    public bool HasErrors => _validator.HasErrors;

    /// <inheritdoc />
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <inheritdoc />
    public IEnumerable GetErrors(string? propertyName) => _validator.GetErrors(propertyName);

    /// <summary>
    /// Clears all validation errors maintained by the view model.
    /// </summary>
    protected void ClearErrors() => _validator.ClearErrors();

    /// <summary>
    /// Validates all observable properties decorated with validation attributes.
    /// </summary>
    protected void ValidateAllProperties() => _validator.ValidateAllProperties();

    /// <summary>
    /// Validates the specified property value using data annotations.
    /// </summary>
    protected void ValidateProperty<T>(T value, [CallerMemberName] string? propertyName = null)
        => _validator.ValidateProperty(value, propertyName);

    /// <summary>
    /// Allows derived classes to react to busy state transitions.
    /// </summary>
    /// <param name="value">The new busy state.</param>
    protected virtual void OnIsBusyChanged(bool value)
    {
    }

    private sealed class ObservableValidatorProxy : ObservableValidator
    {
        public new void ValidateProperty<T>(T value, [CallerMemberName] string? propertyName = null)
            => base.ValidateProperty(value, propertyName);

        public new void ValidateAllProperties() => base.ValidateAllProperties();

        public new void ClearErrors() => base.ClearErrors();
    }
}
// END CHANGE Veriado.WinUI/ViewModels/BaseViewModel.cs
