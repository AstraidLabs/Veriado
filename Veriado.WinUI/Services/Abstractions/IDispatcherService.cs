using System;
using System.Threading.Tasks;

namespace Veriado.WinUI.Services.Abstractions;

public interface IDispatcherService
{
    bool HasThreadAccess { get; }

    Task RunAsync(Action action);
}
