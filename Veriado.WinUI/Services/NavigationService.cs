using System;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Veriado.WinUI.Messages;
using Veriado.WinUI.Views;

namespace Veriado.WinUI.Services;

/// <summary>
/// WinUI implementation of <see cref="INavigationService"/> that coordinates page navigation.
/// </summary>
public sealed class NavigationService : INavigationService, IRecipient<OpenFileDetailMessage>
{
    private readonly IMessenger _messenger;
    private Frame? _frame;

    public NavigationService(IMessenger messenger)
    {
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _messenger.Register<NavigationService, OpenFileDetailMessage>(this, static (recipient, message) => recipient.Receive(message));
    }

    /// <inheritdoc />
    public void Initialize(Frame frame)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    /// <inheritdoc />
    public void NavigateToFiles()
        => Navigate(typeof(FilesPage));

    /// <inheritdoc />
    public void NavigateToImport()
        => Navigate(typeof(ImportPage));

    /// <inheritdoc />
    public void NavigateToDetail(Guid fileId)
        => Navigate(typeof(FileDetailPage), fileId);

    /// <inheritdoc />
    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
        {
            _frame.GoBack();
        }
    }

    /// <inheritdoc />
    public void Receive(OpenFileDetailMessage message)
    {
        NavigateToDetail(message.FileId);
    }

    private void Navigate(Type pageType, object? parameter = null)
    {
        if (_frame is null)
        {
            throw new InvalidOperationException("Navigation service has not been initialized.");
        }

        _frame.Navigate(pageType, parameter, new DrillInNavigationTransitionInfo());
    }
}
