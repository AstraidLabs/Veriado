using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Veriado.WinUI.ViewModels.Files;
using Windows.UI;

namespace Veriado.WinUI.Views.Files;

public sealed partial class FileDetailDialog : UserControl
{
    private FileDetailDialogViewModel? _viewModel;
    private Brush? _errorBrush;

    public FileDetailDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as FileDetailDialogViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(null);
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        AttachViewModel(args.NewValue as FileDetailDialogViewModel);
    }

    private void OnViewModelErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(e.PropertyName))
        {
            UpdateAllValidationStates();
        }
        else
        {
            UpdateValidationState(e.PropertyName);
        }
    }

    private void AttachViewModel(FileDetailDialogViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.ErrorsChanged -= OnViewModelErrorsChanged;
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.ErrorsChanged += OnViewModelErrorsChanged;
            UpdateAllValidationStates();
        }
        else
        {
            ClearAllValidationStates();
        }
    }

    private void UpdateAllValidationStates()
    {
        UpdateValidationState(nameof(FileDetailDialogViewModel.Mime));
        UpdateValidationState(nameof(FileDetailDialogViewModel.Author));
        UpdateValidationState(nameof(FileDetailDialogViewModel.ValidityIssuedDate));
        UpdateValidationState(nameof(FileDetailDialogViewModel.ValidityUntilDate));
    }

    private void UpdateValidationState(string propertyName)
    {
        if (_viewModel is null)
        {
            return;
        }

        var errors = _viewModel
            .GetErrors(propertyName)
            .OfType<string>()
            .Where(static error => !string.IsNullOrWhiteSpace(error))
            .ToList();

        switch (propertyName)
        {
            case nameof(FileDetailDialogViewModel.Mime):
                ApplyErrors(MimeTextBox, errors);
                break;
            case nameof(FileDetailDialogViewModel.Author):
                ApplyErrors(AuthorTextBox, errors);
                break;
            case nameof(FileDetailDialogViewModel.ValidityIssuedDate):
                ApplyErrors(ValidityFromDatePicker, errors);
                break;
            case nameof(FileDetailDialogViewModel.ValidityUntilDate):
                ApplyErrors(ValidityToDatePicker, errors);
                break;
        }
    }

    private void ClearAllValidationStates()
    {
        ApplyErrors(MimeTextBox, Array.Empty<string>());
        ApplyErrors(AuthorTextBox, Array.Empty<string>());
        ApplyErrors(ValidityFromDatePicker, Array.Empty<string>());
        ApplyErrors(ValidityToDatePicker, Array.Empty<string>());
    }

    private void ApplyErrors(Control? control, IReadOnlyCollection<string> errors)
    {
        if (control is null)
        {
            return;
        }

        if (errors.Count == 0)
        {
            ToolTipService.SetToolTip(control, null);
            ClearErrorStyling(control);
            return;
        }

        var message = string.Join(Environment.NewLine, errors);
        ToolTipService.SetToolTip(control, message);
        ApplyErrorStyling(control);
    }

    private void ApplyErrorStyling(Control control)
    {
        var brush = GetErrorBrush();
        switch (control)
        {
            case TextBox textBox:
                textBox.BorderBrush = brush;
                break;
            case DatePicker datePicker:
                datePicker.BorderBrush = brush;
                break;
        }
    }

    private static void ClearErrorStyling(Control control)
    {
        switch (control)
        {
            case TextBox textBox:
                textBox.ClearValue(TextBox.BorderBrushProperty);
                break;
            case DatePicker datePicker:
                datePicker.ClearValue(DatePicker.BorderBrushProperty);
                break;
        }
    }

    private Brush GetErrorBrush()
    {
        if (_errorBrush is not null)
        {
            return _errorBrush;
        }

        if (Application.Current?.Resources.TryGetValue("SystemFillColorCriticalBrush", out var resource) == true && resource is Brush brush)
        {
            _errorBrush = brush;
        }
        else
        {
            _errorBrush = new SolidColorBrush(Colors.Red);
        }

        return _errorBrush;
    }
}
