using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Veriado.Infrastructure.Lifecycle;

namespace Veriado.Infrastructure.Diagnostics;

public interface IAppHealthMonitor
{
    AppLifecycleState? CurrentLifecycleState { get; }

    void ReportLifecycleState(AppLifecycleState state);

    void ReportBackgroundState(string serviceName, BackgroundServiceRunState state, string? message = null);

    void ReportBackgroundIteration(
        string serviceName,
        BackgroundIterationOutcome outcome,
        TimeSpan? duration = null,
        Exception? exception = null,
        string? message = null);

    IReadOnlyCollection<BackgroundServiceSnapshot> GetBackgroundSnapshots();
}

public enum BackgroundServiceRunState
{
    Starting,
    Running,
    Paused,
    Stopping,
    Stopped,
    Faulted,
}

public enum BackgroundIterationOutcome
{
    None,
    Success,
    NoWork,
    Timeout,
    Failed,
    Canceled,
}

public sealed record BackgroundServiceSnapshot(
    string ServiceName,
    BackgroundServiceRunState State,
    BackgroundIterationOutcome LastOutcome,
    DateTimeOffset TimestampUtc,
    TimeSpan? LastDuration,
    string? Message,
    Exception? Exception);

public sealed class AppHealthMonitor : IAppHealthMonitor
{
    private readonly ConcurrentDictionary<string, BackgroundServiceSnapshot> _services = new(StringComparer.OrdinalIgnoreCase);
    private AppLifecycleState? _lifecycleState;

    public AppLifecycleState? CurrentLifecycleState => Volatile.Read(ref _lifecycleState);

    public void ReportLifecycleState(AppLifecycleState state)
    {
        Volatile.Write(ref _lifecycleState, state);
    }

    public void ReportBackgroundState(string serviceName, BackgroundServiceRunState state, string? message = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        _services.AddOrUpdate(
            serviceName,
            name => new BackgroundServiceSnapshot(
                name,
                state,
                BackgroundIterationOutcome.None,
                DateTimeOffset.UtcNow,
                null,
                message,
                null),
            (_, snapshot) => snapshot with
            {
                State = state,
                TimestampUtc = DateTimeOffset.UtcNow,
                Message = message,
                Exception = state == BackgroundServiceRunState.Faulted ? snapshot.Exception : null,
            });
    }

    public void ReportBackgroundIteration(
        string serviceName,
        BackgroundIterationOutcome outcome,
        TimeSpan? duration = null,
        Exception? exception = null,
        string? message = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        _services.AddOrUpdate(
            serviceName,
            name => new BackgroundServiceSnapshot(
                name,
                BackgroundServiceRunState.Running,
                outcome,
                DateTimeOffset.UtcNow,
                duration,
                message,
                exception),
            (_, snapshot) => snapshot with
            {
                LastOutcome = outcome,
                LastDuration = duration,
                TimestampUtc = DateTimeOffset.UtcNow,
                Message = message,
                Exception = exception,
            });
    }

    public IReadOnlyCollection<BackgroundServiceSnapshot> GetBackgroundSnapshots()
    {
        return _services.Values;
    }
}
