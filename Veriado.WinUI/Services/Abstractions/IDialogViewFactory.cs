using Microsoft.UI.Xaml.Controls;

namespace Veriado.WinUI.Services.Abstractions;

/// <summary>
/// Provides a factory abstraction for creating dialog instances bound to view models.
/// </summary>
public interface IDialogViewFactory
{
    bool CanCreate(object viewModel);

    ContentDialog Create(object viewModel);
}
