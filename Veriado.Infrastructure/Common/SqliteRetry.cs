using Microsoft.Data.Sqlite;

namespace Veriado.Infrastructure.Common;

/// <summary>
/// Provides a unified retry helper for operations that may encounter SQLITE_BUSY.
/// </summary>
public static class SqliteRetry
{
    private const int SqliteBusyErrorCode = 5;
    private const int MaxAttempts = 5;
    private const double InitialBackoffMilliseconds = 25d;
    private const double MaxBackoffMilliseconds = 400d;

    public static Task ExecuteAsync(
        Func<Task> operation,
        Func<SqliteException, int, TimeSpan, Task>? onRetryAsync,
        Action<SqliteException, int>? onGiveUp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteCoreAsync(
            async () =>
            {
                await operation().ConfigureAwait(false);
                return true;
            },
            onRetryAsync,
            onGiveUp,
            cancellationToken);
    }

    public static Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        Func<SqliteException, int, TimeSpan, Task>? onRetryAsync,
        Action<SqliteException, int>? onGiveUp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteCoreAsync(operation, onRetryAsync, onGiveUp, cancellationToken);
    }

    private static async Task<T> ExecuteCoreAsync<T>(
        Func<Task<T>> operation,
        Func<SqliteException, int, TimeSpan, Task>? onRetryAsync,
        Action<SqliteException, int>? onGiveUp,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var delay = TimeSpan.FromMilliseconds(InitialBackoffMilliseconds);

        while (true)
        {
            attempt++;

            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteBusyErrorCode)
            {
                if (attempt >= MaxAttempts)
                {
                    onGiveUp?.Invoke(ex, attempt);
                    throw;
                }

                if (onRetryAsync is not null)
                {
                    await onRetryAsync(ex, attempt, delay).ConfigureAwait(false);
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                var nextDelay = Math.Min(delay.TotalMilliseconds * 2, MaxBackoffMilliseconds);
                delay = TimeSpan.FromMilliseconds(nextDelay);
            }
        }
    }
}
