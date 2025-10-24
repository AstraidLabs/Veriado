using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Veriado.Services.Import.Ingestion;

/// <summary>
/// Provides helper utilities for opening files with retry and diagnostics.
/// </summary>
internal static class FileOpener
{
    public static async Task<FileStream> OpenForReadWithRetryAsync(
        string path,
        ImportOptions options,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(options);

        var normalizedBufferSize = Math.Max(options.BufferSize, 4096);
        var share = ResolveShare(options.SharePolicy);
        var attempts = 0;
        var delay = NormalizeDelay(options.RetryBaseDelay);
        var maxDelay = NormalizeDelay(options.MaxRetryDelay);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    share,
                    normalizedBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (attempts > 0 && logger is not null)
                {
                    logger.LogDebug(
                        "Successfully opened {File} for reading after {Attempts} retries.",
                        path,
                        attempts);
                }

                return stream;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempts < options.MaxRetryCount)
            {
                attempts++;
                var nextDelay = TimeSpan.FromMilliseconds(Math.Min(maxDelay.TotalMilliseconds, delay.TotalMilliseconds));
                if (logger is not null)
                {
                    logger.LogWarning(
                        ex,
                        "Encountered transient error while opening {File}. Retrying attempt {Attempt}/{MaxAttempts} after {Delay}.",
                        path,
                        attempts,
                        options.MaxRetryCount,
                        nextDelay);
                }

                await Task.Delay(nextDelay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(maxDelay.TotalMilliseconds, delay.TotalMilliseconds * 2));
                continue;
            }
        }
    }

    private static FileShare ResolveShare(FileOpenSharePolicy policy)
        => policy switch
        {
            FileOpenSharePolicy.ReadOnly => FileShare.Read,
            FileOpenSharePolicy.ReadWrite => FileShare.ReadWrite,
            FileOpenSharePolicy.ReadWriteDelete => FileShare.ReadWrite | FileShare.Delete,
            _ => FileShare.Read,
        };

    private static bool IsRetryable(Exception ex)
    {
        if (ex is IOException io)
        {
            var hresult = io.HResult;
            return hresult == ESharingViolation || hresult == ELockViolation;
        }

        if (ex is UnauthorizedAccessException)
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        return false;
    }

    private static int ESharingViolation => unchecked((int)0x80070020);
    private static int ELockViolation => unchecked((int)0x80070021);

    private static TimeSpan NormalizeDelay(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return TimeSpan.FromMilliseconds(100);
        }

        return value;
    }
}
