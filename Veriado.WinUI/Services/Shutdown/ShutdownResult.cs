using System;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services.Shutdown;

public enum ShutdownStatus
{
    Success,
    Canceled,
    Failed,
}

public enum ShutdownFailurePhase
{
    None,
    LifecycleStop,
    HostStop,
    HostDispose,
}

public enum ShutdownFailureReason
{
    None,
    Timeout,
    Canceled,
    Exception,
    NotSupported,
    Unknown,
}

public sealed record class ShutdownFailureDetail(
    ShutdownFailurePhase Phase,
    ShutdownFailureReason Reason,
    Exception? Exception = null)
{
    public static ShutdownFailureDetail Timeout(ShutdownFailurePhase phase, Exception? exception = null) =>
        new(phase, ShutdownFailureReason.Timeout, exception);

    public static ShutdownFailureDetail Canceled(ShutdownFailurePhase phase, Exception? exception = null) =>
        new(phase, ShutdownFailureReason.Canceled, exception);

    public static ShutdownFailureDetail Error(ShutdownFailurePhase phase, Exception? exception = null) =>
        new(phase, ShutdownFailureReason.Exception, exception);

    public static ShutdownFailureDetail NotSupported(ShutdownFailurePhase phase) =>
        new(phase, ShutdownFailureReason.NotSupported);

    public static ShutdownFailureDetail Unknown(ShutdownFailurePhase phase, Exception? exception = null) =>
        new(phase, ShutdownFailureReason.Unknown, exception);
}

public sealed record class ShutdownResult(
    ShutdownStatus Status,
    TimeSpan Duration,
    bool LifecycleStopped,
    HostShutdownResult Host,
    ShutdownFailureDetail? Failure = null)
{
    public bool IsAllowed => Status == ShutdownStatus.Success;

    public static ShutdownResult Success(TimeSpan duration, bool lifecycleStopped, HostShutdownResult host) =>
        new(ShutdownStatus.Success, duration, lifecycleStopped, host);

    public static ShutdownResult Canceled(TimeSpan duration) =>
        new(ShutdownStatus.Canceled, duration, LifecycleStopped: false, Host: default);

    public static ShutdownResult Failed(
        ShutdownFailureDetail failure,
        TimeSpan duration,
        bool lifecycleStopped,
        HostShutdownResult host) =>
        new(ShutdownStatus.Failed, duration, lifecycleStopped, host, failure);
}
