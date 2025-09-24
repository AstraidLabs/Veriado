using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class DispatcherService : IDispatcherService
{
    private readonly TaskCompletionSource<DispatcherQueue> _dispatcherReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _dispatcherLock = new();
    private DispatcherQueue? _dispatcher;

    public DispatcherService()
    {
    }

    public bool HasThreadAccess
    {
        get
        {
            var dispatcher = Volatile.Read(ref _dispatcher);
            if (dispatcher is not null)
            {
                return dispatcher.HasThreadAccess;
            }

            if (_dispatcherReady.Task.IsCompletedSuccessfully)
            {
                return _dispatcherReady.Task.Result.HasThreadAccess;
            }

            return false;
        }
    }

    public void ResetDispatcher(DispatcherQueue dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        lock (_dispatcherLock)
        {
            if (_dispatcher is not null)
            {
                if (ReferenceEquals(_dispatcher, dispatcher))
                {
                    return;
                }

                if (_dispatcherReady.Task.IsCompleted)
                {
                    throw new InvalidOperationException("The dispatcher has already been initialized.");
                }
            }

            SetDispatcherUnsafe(dispatcher);
        }
    }

    public async Task Enqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = await GetDispatcherAsync().ConfigureAwait(true);
        if (dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() => Execute(action, completion)))
        {
            completion.SetException(new InvalidOperationException("Unable to enqueue action on the UI dispatcher."));
        }

        await completion.Task.ConfigureAwait(true);
    }

    public async Task EnqueueAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await EnqueueAsyncInternal(action).ConfigureAwait(true);
    }

    public async Task<T> EnqueueAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return await EnqueueAsyncInternal(action).ConfigureAwait(true);
    }

    private async Task EnqueueAsyncInternal(Func<Task> action)
    {
        var dispatcher = await GetDispatcherAsync().ConfigureAwait(true);
        if (dispatcher.HasThreadAccess)
        {
            var task = action();
            if (task is null)
            {
                throw new InvalidOperationException("The dispatcher action returned a null task.");
            }

            await task.ConfigureAwait(true);
            return;
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() => ExecuteAsync(action, completion)))
        {
            completion.SetException(new InvalidOperationException("Unable to enqueue action on the UI dispatcher."));
        }

        await completion.Task.ConfigureAwait(true);
    }

    private async Task<T> EnqueueAsyncInternal<T>(Func<Task<T>> action)
    {
        var dispatcher = await GetDispatcherAsync().ConfigureAwait(true);
        if (dispatcher.HasThreadAccess)
        {
            var task = action();
            if (task is null)
            {
                throw new InvalidOperationException("The dispatcher action returned a null task.");
            }

            return await task.ConfigureAwait(true);
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() => ExecuteAsync(action, completion)))
        {
            completion.SetException(new InvalidOperationException("Unable to enqueue action on the UI dispatcher."));
        }

        return await completion.Task.ConfigureAwait(true);
    }

    private async Task<DispatcherQueue> GetDispatcherAsync()
    {
        var dispatcher = Volatile.Read(ref _dispatcher);
        if (dispatcher is not null)
        {
            return dispatcher;
        }

        return await _dispatcherReady.Task.ConfigureAwait(false);
    }

    private void SetDispatcherUnsafe(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        if (!_dispatcherReady.Task.IsCompleted)
        {
            _dispatcherReady.SetResult(dispatcher);
        }
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
