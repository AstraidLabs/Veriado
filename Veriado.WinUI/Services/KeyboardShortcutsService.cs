using System;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.Services.Messages;
using Windows.System;

namespace Veriado.WinUI.Services;

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
            return;

        if (!_windowProvider.TryGetWindow(out var window) || window is null)
            return;

        // KeyboardAccelerators patøí na UIElement (napø. koøen Content okna)
        if (window.Content is not UIElement root)
            return;

        var openSearch = new KeyboardAccelerator
        {
            Key = VirtualKey.Space,
            Modifiers = VirtualKeyModifiers.Control,
            ScopeOwner = root // volitelné, zúží rozsah
        };
        openSearch.Invoked += (_, args) =>
        {
            _messenger.Send(new FocusSearchRequestedMessage());
            args.Handled = true;
        };

        var closeSearch = new KeyboardAccelerator
        {
            Key = VirtualKey.Escape,
            ScopeOwner = root
        };
        closeSearch.Invoked += (_, args) =>
        {
            _messenger.Send(new CloseSearchOverlayMessage());
            args.Handled = true;
        };

        root.KeyboardAccelerators.Add(openSearch);
        root.KeyboardAccelerators.Add(closeSearch);

        _registered = true;
    }
}
