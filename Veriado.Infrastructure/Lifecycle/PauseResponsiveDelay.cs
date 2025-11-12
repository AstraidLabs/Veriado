using System;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Infrastructure.Lifecycle;

public static class PauseResponsiveDelay
{
    public static async Task DelayAsync(TimeSpan delay, PauseToken pauseToken, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        if (pauseToken.IsPaused)
        {
            await pauseToken.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(delay, linkedCts.Token);

        if (!pauseToken.IsPaused)
        {
            Task pauseTask;
            try
            {
                pauseTask = pauseToken.WhenPausedAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            var completed = await Task.WhenAny(delayTask, pauseTask).ConfigureAwait(false);
            if (completed == pauseTask)
            {
                linkedCts.Cancel();
                await pauseToken.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        await delayTask.ConfigureAwait(false);
    }
}
