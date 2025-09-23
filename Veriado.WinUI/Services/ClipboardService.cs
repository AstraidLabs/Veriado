using System;
using System.Threading.Tasks;
using Veriado.Services.Abstractions;
using Windows.ApplicationModel.DataTransfer;

namespace Veriado.Services;

public sealed class ClipboardService : IClipboardService
{
    private readonly IDispatcherService _dispatcher;

    public ClipboardService(IDispatcherService dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task CopyTextAsync(string text)
    {
        await _dispatcher.Enqueue(() =>
        {
            if (string.IsNullOrEmpty(text))
            {
                Clipboard.Clear();
                return;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);
        }).ConfigureAwait(false);
    }
}
