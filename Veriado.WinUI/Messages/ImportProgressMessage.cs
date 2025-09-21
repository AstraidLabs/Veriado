// BEGIN CHANGE Veriado.WinUI/Messages/ImportProgressMessage.cs
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Veriado.WinUI.Messages;

/// <summary>
/// Represents progress notifications emitted by the import workflow.
/// </summary>
/// <param name="ProgressValue">The optional progress value between 0 and 100.</param>
/// <param name="Message">The human readable status message.</param>
/// <param name="IsIndeterminate">Indicates whether the operation progress is indeterminate.</param>
/// <param name="IsCompleted">Indicates whether the operation has finished (successfully or otherwise).</param>
public sealed record ImportProgress(double? ProgressValue, string? Message, bool IsIndeterminate, bool IsCompleted)
{
    public static ImportProgress Started(string? message = null)
        => new(null, message, true, false);

    public static ImportProgress Completed(string? message = null)
        => new(100d, message, false, true);

    public static ImportProgress Failed(string? message = null)
        => new(null, message, false, true);
}

/// <summary>
/// Messenger payload carrying <see cref="ImportProgress"/> updates.
/// </summary>
public sealed class ImportProgressMessage : ValueChangedMessage<ImportProgress>
{
    public ImportProgressMessage(ImportProgress value)
        : base(value)
    {
    }
}
// END CHANGE Veriado.WinUI/Messages/ImportProgressMessage.cs
