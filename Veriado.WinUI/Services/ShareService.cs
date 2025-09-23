using System;
using System.Threading;
using System.Threading.Tasks;
using Veriado.WinUI.Services.Abstractions;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT.Interop;

namespace Veriado.WinUI.Services;

public sealed class ShareService : IShareService
{
    private readonly IWindowProvider _windowProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ShareService(IWindowProvider windowProvider)
    {
        _windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
    }

    public async Task ShareTextAsync(string title, string text, CancellationToken cancellationToken = default)
    {
        await ShareAsync(title, async request =>
        {
            request.Data.SetText(text ?? string.Empty);
            await Task.CompletedTask.ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ShareFileAsync(string title, string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentNullException(nameof(filePath));
        }

        var storageFile = await StorageFile.GetFileFromPathAsync(filePath).AsTask(cancellationToken).ConfigureAwait(false);

        await ShareAsync(title, async request =>
        {
            request.Data.SetStorageItems(new[] { storageFile });
            await Task.CompletedTask.ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task ShareAsync(string title, Func<DataRequest, Task> populateRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(populateRequest);

        if (!_windowProvider.TryGetWindow(out var window) || window is null)
        {
            throw new InvalidOperationException("Window has not been initialized.");
        }

        var hwnd = _windowProvider.GetHwnd(window);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manager = DataTransferManagerInterop.GetForWindow(hwnd, typeof(DataTransferManager).GUID);
            TaskCompletionSource<object?>? completion = new();

            void Handler(DataTransferManager sender, DataRequestedEventArgs args)
            {
                sender.DataRequested -= Handler;
                var request = args.Request;
                request.Data.Properties.Title = string.IsNullOrWhiteSpace(title) ? "Sdílet" : title;
                populateRequest(request).ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception is not null)
                    {
                        completion.TrySetException(t.Exception);
                    }
                    else if (t.IsCanceled)
                    {
                        completion.TrySetCanceled();
                    }
                    else
                    {
                        completion.TrySetResult(null);
                    }
                }, TaskScheduler.Default);
            }

            manager.DataRequested += Handler;

            DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
