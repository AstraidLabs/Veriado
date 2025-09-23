using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class DispatcherService : IDispatcherService
{
    private readonly IWindowProvider _windowProvider;
    private DispatcherQueue? _dispatcher;

    public DispatcherService(IWindowProvider windowProvider)
    {
        _windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
    }

    public bool HasThreadAccess => GetDispatcher().HasThreadAccess;

    public Task Enqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = GetDispatcher();
        if (dispatcher.HasThreadAccess)
        {
            try
            {
                action();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() => Execute(action, completion)))
        {
            completion.SetException(new InvalidOperationException("Unable to enqueue action on the UI dispatcher."));
        }

        return completion.Task;
    }

    public Task EnqueueAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return EnqueueAsyncInternal(action);
    }

    public Task<T> EnqueueAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return EnqueueAsyncInternal(action);
    }

    private Task EnqueueAsyncInternal(Func<Task> action)
    {
        var dispatcher = GetDispatcher();
        if (dispatcher.HasThreadAccess)
        {
            try
            {
                var task = action();
                return task ?? Task.FromException(new InvalidOperationException("The dispatcher action returned a null task."));
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() => ExecuteAsync(action, completion)))
        {
            completion.SetException(new InvalidOperationException("Unable to enqueue action on the UI dispatcher."));
        }

        return completion.Task;
    }

    private Task<T> EnqueueAsyncInternal<T>(Func<Task<T>> action)
    {
        var dispatcher = GetDispatcher();
        if (dispatcher.HasThreadAccess)
        {
            try
            {
                var task = action();
                return task ?? Task.FromException<T>(new InvalidOperationException("The dispatcher action returned a null task."));
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() => ExecuteAsync(action, completion)))
        {
            completion.SetException(new InvalidOperationException("Unable to enqueue action on the UI dispatcher."));
        }

        return completion.Task;
    }

    private DispatcherQueue GetDispatcher()
    {
        if (_dispatcher is not null)
        {
            return _dispatcher;
        }

        if (_windowProvider.TryGetWindow(out var window) && window is not null)
        {
            _dispatcher = window.DispatcherQueue;
        }
        else
        {
            _dispatcher = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("UI dispatcher is not available.");
        }

        return _dispatcher;
    }

    private static void Execute(Action action, TaskCompletionSource<object?> completion)
    {
        try
        {
            action();
            completion.SetResult(null);
        }
        catch (Exception ex)
        {
            completion.SetException(ex);
        }
    }

    private static async void ExecuteAsync(Func<Task> action, TaskCompletionSource<object?> completion)
    {
        try
        {
            var task = action();
            if (task is null)
            {
                completion.SetException(new InvalidOperationException("The dispatcher action returned a null task."));
                return;
            }

            await task.ConfigureAwait(true);
            completion.SetResult(null);
        }
        catch (Exception ex)
        {
            completion.SetException(ex);
        }
    }

    private static async void ExecuteAsync<T>(Func<Task<T>> action, TaskCompletionSource<T> completion)
    {
        try
        {
            var task = action();
            if (task is null)
            {
                completion.SetException(new InvalidOperationException("The dispatcher action returned a null task."));
                return;
            }

            var result = await task.ConfigureAwait(true);
            completion.SetResult(result);
        }
        catch (Exception ex)
        {
            completion.SetException(ex);
        }
    }
}
