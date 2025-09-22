using System;

namespace Veriado.WinUI.Messages;

/// <summary>
/// Message instructing the navigation layer to open the file detail page.
/// </summary>
/// <param name="FileId">The identifier of the file to display.</param>
public sealed record OpenFileDetailMessage(Guid FileId);
