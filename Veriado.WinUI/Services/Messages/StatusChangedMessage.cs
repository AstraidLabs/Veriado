namespace Veriado.WinUI.Services.Messages;

public sealed record StatusChangedMessage(bool HasError, string? Message);
