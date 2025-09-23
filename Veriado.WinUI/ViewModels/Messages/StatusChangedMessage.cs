namespace Veriado.WinUI.ViewModels.Messages;

/// <summary>
/// Represents a status update emitted by child view models to display feedback in the shell.
/// </summary>
/// <param name="Text">The status message to display.</param>
/// <param name="IsError">Indicates whether the status represents an error.</param>
public sealed record StatusChangedMessage(string? Text, bool IsError);
