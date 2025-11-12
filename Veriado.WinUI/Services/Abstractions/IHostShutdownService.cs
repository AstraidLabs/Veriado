using System;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.WinUI.Services.Abstractions;

public interface IHostShutdownService
{
    Task<HostStopResult> StopAsync(TimeSpan timeout, CancellationToken cancellationToken);

    ValueTask<HostDisposeResult> DisposeAsync();
}

public enum HostStopState
{
    Completed,
    AlreadyStopped,
    NotInitialized,
    TimedOut,
    Canceled,
    Failed,
}

public readonly record struct HostStopResult(HostStopState State, Exception? Exception = null)
{
    public bool IsSuccess => State is HostStopState.Completed or HostStopState.AlreadyStopped or HostStopState.NotInitialized;

    public static HostStopResult Completed() => new(HostStopState.Completed);

    public static HostStopResult AlreadyStopped(Exception? exception = null) => new(HostStopState.AlreadyStopped, exception);

    public static HostStopResult NotInitialized(Exception? exception = null) => new(HostStopState.NotInitialized, exception);

    public static HostStopResult TimedOut(Exception? exception = null) => new(HostStopState.TimedOut, exception);

    public static HostStopResult Canceled(Exception? exception = null) => new(HostStopState.Canceled, exception);

    public static HostStopResult Failed(Exception exception) => new(HostStopState.Failed, exception);
}

public enum HostDisposeState
{
    Completed,
    AlreadyDisposed,
    NotInitialized,
    Failed,
}

public readonly record struct HostDisposeResult(HostDisposeState State, Exception? Exception = null)
{
    public bool IsSuccess => State is HostDisposeState.Completed or HostDisposeState.AlreadyDisposed or HostDisposeState.NotInitialized;

    public static HostDisposeResult Completed() => new(HostDisposeState.Completed);

    public static HostDisposeResult AlreadyDisposed(Exception? exception = null) => new(HostDisposeState.AlreadyDisposed, exception);

    public static HostDisposeResult NotInitialized(Exception? exception = null) => new(HostDisposeState.NotInitialized, exception);

    public static HostDisposeResult Failed(Exception exception) => new(HostDisposeState.Failed, exception);
}
