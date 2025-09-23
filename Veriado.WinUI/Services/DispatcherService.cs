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

    public Task RunAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = GetDispatcher();
        if (dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<object?>();
        if (!dispatcher.TryEnqueue(() =>
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
            }))
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

        if (_windowProvider.TryGetWindow() is { } window)
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
}
