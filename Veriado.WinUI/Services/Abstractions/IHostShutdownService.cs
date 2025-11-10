namespace Veriado.WinUI.Services.Abstractions;

public interface IHostShutdownService
{
    Task StopAsync(CancellationToken cancellationToken);

    ValueTask DisposeAsync();
}
