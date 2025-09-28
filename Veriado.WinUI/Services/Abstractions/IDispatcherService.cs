using Microsoft.UI.Dispatching;

namespace Veriado.WinUI.Services.Abstractions;

public interface IDispatcherService
{
    bool HasThreadAccess { get; }

    void ResetDispatcher(DispatcherQueue dispatcher);

    Task Enqueue(Action action);

    Task EnqueueAsync(Func<Task> action);

    Task<T> EnqueueAsync<T>(Func<Task<T>> action);
}
