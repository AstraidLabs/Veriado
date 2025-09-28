using Microsoft.UI.Dispatching;

namespace Veriado.WinUI.Infrastructure;

public interface IUiDispatcher
{
    bool HasThreadAccess { get; }
    Task RunAsync(Action action);
    Task<T> RunAsync<T>(Func<T> func);
}

public sealed class UiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue;

    public UiDispatcher(DispatcherQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public bool HasThreadAccess => _queue.HasThreadAccess;

    public Task RunAsync(Action action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<object?>();
        _queue.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    public Task<T> RunAsync<T>(Func<T> func)
    {
        if (func is null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        if (HasThreadAccess)
        {
            return Task.FromResult(func());
        }

        var tcs = new TaskCompletionSource<T>();
        _queue.TryEnqueue(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }
}
