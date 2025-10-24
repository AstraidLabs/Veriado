using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Veriado.Appl.Abstractions;

namespace Veriado.Services.Import.Ingestion;

/// <summary>
/// Streams file content into the configured storage writer without materialising it in memory.
/// </summary>
public sealed class StreamingFileIngestor : IFileIngestor
{
    private readonly IStorageWriter _storage;
    private readonly ILogger<StreamingFileIngestor> _logger;

    public StreamingFileIngestor(IStorageWriter storage, ILogger<StreamingFileIngestor>? logger = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? NullLogger<StreamingFileIngestor>.Instance;
    }

    public async Task<FileIngestResult> IngestAsync(FileIngestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var options = request.Options ?? new ImportOptions();
        var bufferSize = Math.Max(options.BufferSize, 16 * 1024);

        var reservation = await _storage
            .ReservePathAsync(request.PreferredStoragePath, cancellationToken)
            .ConfigureAwait(false);

        await using var source = await FileOpener
            .OpenForReadWithRetryAsync(request.SourcePath, options, _logger, cancellationToken)
            .ConfigureAwait(false);
        await using var destination = await _storage
            .OpenWriteAsync(reservation, cancellationToken)
            .ConfigureAwait(false);

        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        using var sha256 = SHA256.Create();
        using var sha1 = SHA1.Create();
        long totalBytes = 0;

        try
        {
            while (true)
            {
                var read = await source
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await destination
                    .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);

                _ = sha256.TransformBlock(buffer, 0, read, outputBuffer: null, outputOffset: 0);
                _ = sha1.TransformBlock(buffer, 0, read, outputBuffer: null, outputOffset: 0);
                totalBytes += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

        var sha256Hex = Convert.ToHexString(sha256.Hash ?? Array.Empty<byte>());
        var sha1Hex = Convert.ToHexString(sha1.Hash ?? Array.Empty<byte>());
        var commitContext = new StorageCommitContext(totalBytes, sha256Hex, sha1Hex);

        var storageResult = await _storage
            .CommitAsync(reservation, commitContext, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(storageResult.Hash.Value, sha256Hex, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Storage hash differs from computed SHA-256 hash.");
        }

        if (storageResult.Size.Value != totalBytes)
        {
            throw new InvalidOperationException("Storage size differs from streamed length.");
        }

        _logger.LogDebug(
            "Streamed {Length} bytes from {Source} to {Destination} (SHA256 {Hash}).",
            totalBytes,
            request.SourcePath,
            storageResult.Path.Value,
            sha256Hex);

        return new FileIngestResult(storageResult, sha1Hex);
    }
}
