using System;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.ViewModels.Base;

/// <summary>
/// Defines a contract for view models hosted within dialogs to signal completion.
/// </summary>
public interface IDialogAware
{
    event EventHandler<DialogResult> CloseRequested;
}
