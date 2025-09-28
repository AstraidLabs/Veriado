using Veriado.WinUI.Services.Messages;

namespace Veriado.WinUI.Services;

public sealed class StatusService : IStatusService
{
    private readonly IMessenger _messenger;

    public StatusService(IMessenger messenger)
    {
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
    }

    public void Info(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            Clear();
            return;
        }

        _messenger.Send(new StatusChangedMessage(false, message));
    }

    public void Error(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            Clear();
            return;
        }

        _messenger.Send(new StatusChangedMessage(true, message));
    }

    public void Clear()
    {
        _messenger.Send(new StatusChangedMessage(false, null));
    }
}
