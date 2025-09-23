using System;
using System.Threading.Tasks;

namespace Veriado.Services.Abstractions;

public interface IDispatcherService
{
    bool HasThreadAccess { get; }

    Task Enqueue(Action action);

    Task EnqueueAsync(Func<Task> action);

    Task<T> EnqueueAsync<T>(Func<Task<T>> action);
}
