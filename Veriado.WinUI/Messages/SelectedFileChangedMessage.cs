// BEGIN CHANGE Veriado.WinUI/Messages/SelectedFileChangedMessage.cs
using System;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Veriado.WinUI.Messages;

/// <summary>
/// Messenger payload indicating that the selected file changed in the grid.
/// </summary>
public sealed class SelectedFileChangedMessage : ValueChangedMessage<Guid?>
{
    public SelectedFileChangedMessage(Guid? value)
        : base(value)
    {
    }
}
// END CHANGE Veriado.WinUI/Messages/SelectedFileChangedMessage.cs
