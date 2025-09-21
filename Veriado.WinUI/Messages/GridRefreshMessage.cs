// BEGIN CHANGE Veriado.WinUI/Messages/GridRefreshMessage.cs
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Veriado.WinUI.Messages;

/// <summary>
/// Describes a request to refresh the file grid.
/// </summary>
/// <param name="ForceReload">Indicates whether the refresh should be forced immediately.</param>
public sealed record GridRefreshRequest(bool ForceReload);

/// <summary>
/// Messenger payload requesting a grid refresh.
/// </summary>
public sealed class GridRefreshMessage : ValueChangedMessage<GridRefreshRequest>
{
    public GridRefreshMessage(GridRefreshRequest value)
        : base(value)
    {
    }
}
// END CHANGE Veriado.WinUI/Messages/GridRefreshMessage.cs
