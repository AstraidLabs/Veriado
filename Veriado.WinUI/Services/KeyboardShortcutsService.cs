using System;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Input;
using Veriado.Services.Abstractions;
using Veriado.Services.Messages;
using Veriado.WinUI.Services.Messages;

namespace Veriado.Services;

public sealed class KeyboardShortcutsService : IKeyboardShortcutsService
{
    private readonly IWindowProvider _windowProvider;
    private readonly IMessenger _messenger;
    private bool _registered;

    public KeyboardShortcutsService(IWindowProvider windowProvider, IMessenger messenger)
    {
        _windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
    }

    public void RegisterDefaultShortcuts()
    {
        if (_registered)
        {
            return;
        }

        if (!_windowProvider.TryGetWindow(out var window) || window is null)
        {
            return;
        }

        var openSearch = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.Space,
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        };
        openSearch.Invoked += (_, args) =>
        {
            _messenger.Send(new OpenSearchOverlayMessage());
            args.Handled = true;
        };

        var closeSearch = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.Escape,
        };
        closeSearch.Invoked += (_, args) =>
        {
            _messenger.Send(new CloseSearchOverlayMessage());
            args.Handled = true;
        };

        window.KeyboardAccelerators.Add(openSearch);
        window.KeyboardAccelerators.Add(closeSearch);
        _registered = true;
    }
}
