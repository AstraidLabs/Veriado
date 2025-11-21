using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Veriado.Infrastructure.Storage;

internal static class AtomicFileOperations
{
    public static async Task CopyAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        cancellationToken.ThrowIfCancellationRequested();

        var tempDestination = destination + $".tmp-{Guid.NewGuid():N}";
        SafePathUtilities.EnsureDirectoryForFile(destination);

        try
        {
            using var sourceStream = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using (var tempStream = File.Open(tempDestination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await sourceStream.CopyToAsync(tempStream, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempDestination, destination, overwrite);
        }
        catch
        {
            TryDelete(tempDestination, null);
            throw;
        }
    }

    public static void TryDelete(string path, ILogger? logger)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to delete temporary file {Path} during cleanup.", path);
        }
    }
}
