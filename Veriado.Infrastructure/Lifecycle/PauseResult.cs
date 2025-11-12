using System;

namespace Veriado.Infrastructure.Lifecycle;

public enum PauseStatus
{
    Succeeded,
    RetryableFailure,
    NotSupported,
}

public readonly record struct PauseResult(
    PauseStatus Status,
    TimeSpan Duration,
    Exception? Exception = null)
{
    public bool IsSuccess => Status == PauseStatus.Succeeded;

    public static PauseResult Success(TimeSpan duration) => new(PauseStatus.Succeeded, duration);

    public static PauseResult RetryableFailure(Exception? exception = null) =>
        new(PauseStatus.RetryableFailure, TimeSpan.Zero, exception);

    public static PauseResult NotSupported(Exception? exception = null) =>
        new(PauseStatus.NotSupported, TimeSpan.Zero, exception);
}
