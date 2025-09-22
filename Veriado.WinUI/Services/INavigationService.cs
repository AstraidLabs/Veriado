using System;
using Microsoft.UI.Xaml.Controls;

namespace Veriado.WinUI.Services;

/// <summary>
/// Provides navigation capabilities for the WinUI shell.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Initializes the navigation service with the root frame instance.
    /// </summary>
    /// <param name="frame">The frame used to host content.</param>
    void Initialize(Frame frame);

    /// <summary>
    /// Navigates to the files page.
    /// </summary>
    void NavigateToFiles();

    /// <summary>
    /// Navigates to the import page.
    /// </summary>
    void NavigateToImport();

    /// <summary>
    /// Navigates to the file detail page for the specified identifier.
    /// </summary>
    /// <param name="fileId">The identifier of the file to display.</param>
    void NavigateToDetail(Guid fileId);

    /// <summary>
    /// Navigates to the previously visited page when possible.
    /// </summary>
    void GoBack();
}
