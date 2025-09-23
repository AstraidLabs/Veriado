using System;

namespace Veriado.WinUI.ViewModels.Messages;

/// <summary>
/// Requests the shell to display a file detail view for the specified identifier.
/// </summary>
/// <param name="FileId">The file identifier to open.</param>
public sealed record OpenFileDetailMessage(Guid FileId);
