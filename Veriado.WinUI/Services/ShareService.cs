using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Services.Abstractions;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using WinRT;
using WinRT.Interop; // MarshalInterface<T>

namespace Veriado.Services;

public sealed class ShareService : IShareService
{
    // === COM interop pro DataTransferManager (oficiální pattern) ===
    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        // Vrací IInspectable* (přes IntPtr), který zabalíme přes WinRT.MarshalInterface
        IntPtr GetForWindow(IntPtr appWindow, ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    private static readonly Guid _dtmIid =
        new(0xA5CAEE9B, 0x8708, 0x49D1, 0x8D, 0x36, 0x67, 0xD2, 0x5A, 0x8D, 0xA0, 0x0C);

    private readonly IWindowProvider _windowProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ShareService(IWindowProvider windowProvider)
    {
        _windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
    }

    public async Task ShareTextAsync(string title, string text, CancellationToken cancellationToken = default)
    {
        await ShareAsync(title, request =>
        {
            request.Data.SetText(text ?? string.Empty);
            return Task.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ShareFileAsync(string title, string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        var storageFile = await StorageFile.GetFileFromPathAsync(filePath).AsTask(cancellationToken).ConfigureAwait(false);

        await ShareAsync(title, request =>
        {
            request.Data.SetStorageItems(new[] { storageFile });
            return Task.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task ShareAsync(string title, Func<DataRequest, Task> populateRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(populateRequest);

        if (!_windowProvider.TryGetWindow(out var window) || window is null)
            throw new InvalidOperationException("Window has not been initialized.");

        var hwnd = _windowProvider.GetHwnd(window);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Získání interop rozhraní a DataTransferManager instance pro konkrétní HWND
            var interop = DataTransferManager.As<IDataTransferManagerInterop>();
            var riid = _dtmIid;
            var dtmPtr = interop.GetForWindow(hwnd, ref riid);
            var manager = MarshalInterface<DataTransferManager>.FromAbi(dtmPtr);

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            TypedEventHandler<DataTransferManager, DataRequestedEventArgs>? handler = null;
            handler = async (sender, args) =>
            {
                try
                {
                    var request = args.Request;
                    request.Data.Properties.Title = string.IsNullOrWhiteSpace(title) ? "Sdílet" : title;

                    var populateTask = populateRequest(request);
                    if (populateTask is null)
                    {
                        tcs.TrySetException(new InvalidOperationException("The share callback returned a null task."));
                        return;
                    }

                    await populateTask.ConfigureAwait(false);
                    tcs.TrySetResult(null);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            };

            manager.DataRequested += handler;

            try
            {
                // Zobrazení Share UI pro dané okno (HWND) přes interop
                interop.ShowShareUIForWindow(hwnd);

                // Ošetření případu, kdy uživatel UI zavře bez výběru cíle (DataRequested se nespustí)
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30), cancellationToken)).ConfigureAwait(false);
                if (completed != tcs.Task)
                {
                    tcs.TrySetCanceled();
                }

                await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                if (handler is not null)
                {
                    manager.DataRequested -= handler;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
