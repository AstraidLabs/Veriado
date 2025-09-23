using System;
using Microsoft.UI.Xaml.Controls;

namespace Veriado.Services;

public interface INavigationService
{
    void Initialize(Frame frame);
    void NavigateToFiles();
    void NavigateToFileDetail(Guid fileId);
    void NavigateToImport();
    bool CanGoBack { get; }
    void GoBack();
}

internal sealed class NavigationService : INavigationService
{
    private Frame? _frame;

    public bool CanGoBack => _frame?.CanGoBack == true;

    public void Initialize(Frame frame)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public void NavigateToFiles()
        => Navigate(typeof(Views.FilesPage));

    public void NavigateToFileDetail(Guid fileId)
        => Navigate(typeof(Views.FileDetailPage), fileId);

    public void NavigateToImport()
        => Navigate(typeof(Views.ImportPage));

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
        {
            _frame.GoBack();
        }
    }

    private void Navigate(Type pageType, object? parameter = null)
    {
        if (_frame is null)
        {
            throw new InvalidOperationException("Navigation service has not been initialized.");
        }

        _frame.Navigate(pageType, parameter);
    }
}
