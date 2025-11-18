using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Veriado.Domain.FileSystem;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.FileSystem;

internal sealed class FileSystemHealthCheckWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<FileSystemHealthCheckWorker> _logger;
    private readonly FileSystemHealthCheckOptions _options;

    public FileSystemHealthCheckWorker(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<FileSystemHealthCheckOptions> options,
        ILogger<FileSystemHealthCheckWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "{WorkerName} started with interval {Interval} and batch size {BatchSize}.",
            nameof(FileSystemHealthCheckWorker),
            _options.Interval,
            _options.BatchSize);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunHealthCheckAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(_options.Interval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("{WorkerName} is stopping (canceled).", nameof(FileSystemHealthCheckWorker));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {WorkerName}.", nameof(FileSystemHealthCheckWorker));
        }
        finally
        {
            _logger.LogInformation("{WorkerName} stopped.", nameof(FileSystemHealthCheckWorker));
        }
    }

    private async Task RunHealthCheckAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var offset = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await dbContext.FileSystems
                .AsTracking()
                .OrderBy(file => file.Id)
                .Skip(offset)
                .Take(_options.BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                return;
            }

            foreach (var file in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EvaluateFileAsync(file, cancellationToken).ConfigureAwait(false);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            dbContext.ChangeTracker.Clear();

            offset += batch.Count;
        }
    }

    private async Task EvaluateFileAsync(FileSystemEntity file, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(file.CurrentFilePath))
        {
            _logger.LogDebug("Skipping file system entity {FileId} because no current path is set.", file.Id);
            return;
        }

        var info = new FileInfo(file.CurrentFilePath);
        if (!info.Exists)
        {
            file.MarkMissing(_clock);
            return;
        }

        var size = ByteSize.From(info.Length);
        var created = UtcTimestamp.From(info.CreationTimeUtc);
        var lastWrite = UtcTimestamp.From(info.LastWriteTimeUtc);
        var lastAccess = UtcTimestamp.From(info.LastAccessTimeUtc);

        var sizeChanged = file.Size != size;
        var timestampsChanged = file.CreatedUtc != created
            || file.LastWriteUtc != lastWrite
            || file.LastAccessUtc != lastAccess;

        if (!sizeChanged && !timestampsChanged)
        {
            file.MarkHealthy();
            return;
        }

        var hash = await ComputeHashAsync(info.FullName, cancellationToken).ConfigureAwait(false);
        if (hash == file.Hash)
        {
            file.UpdateTimestamps(created, lastWrite, lastAccess, UtcTimestamp.From(_clock.UtcNow));
            file.MarkHealthy();
            return;
        }

        file.ReplaceContent(file.RelativePath, hash, size, file.Mime, file.IsEncrypted, lastWrite);
        file.UpdateTimestamps(created, lastWrite, lastAccess, UtcTimestamp.From(_clock.UtcNow));
        file.MarkContentChanged();
    }

    private static async Task<FileHash> ComputeHashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);

        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var hex = Convert.ToHexString(hash.GetHashAndReset());
        return FileHash.From(hex);
    }
}
