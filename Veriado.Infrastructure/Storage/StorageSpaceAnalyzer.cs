using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Appl.Abstractions;

namespace Veriado.Infrastructure.Storage;

public sealed class StorageSpaceAnalyzer : IStorageSpaceAnalyzer
{
    public Task<long> GetAvailableBytesAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(path)) ?? Path.GetPathRoot(Directory.GetCurrentDirectory())!;
        var driveInfo = new DriveInfo(driveRoot);
        return Task.FromResult(driveInfo.AvailableFreeSpace);
    }

    public Task<long> CalculateDirectorySizeAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(path))
        {
            return Task.FromResult(0L);
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
            }
        }

        return Task.FromResult(total);
    }

    public Task<long> GetFileSizeAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(path) ? new FileInfo(path).Length : 0L);
    }
}
