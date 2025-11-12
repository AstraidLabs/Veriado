using System;
using System.Threading;

namespace Veriado.WinUI.Services;

public sealed class ConfirmOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public CancellationToken CancellationToken { get; set; }
        = CancellationToken.None;
}
