using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Appl.Abstractions;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Infrastructure.Storage;

/// <summary>
/// Provides a simple file-system based implementation of <see cref="IFileStorage"/> for local development scenarios.
/// </summary>
internal sealed class LocalFileStorage : IFileStorage, IStorageWriter
{
    private const string DefaultMimeType = "application/octet-stream";
    private readonly string _rootPath;
    private readonly StorageProvider _provider = StorageProvider.Local;

    public LocalFileStorage(InfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.DbPath))
        {
            throw new ArgumentException("Infrastructure options must configure a database path to derive storage location.", nameof(options));
        }

        var baseDirectory = Path.GetDirectoryName(options.DbPath);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        _rootPath = Path.Combine(baseDirectory!, "storage");
        Directory.CreateDirectory(_rootPath);
    }

    public ValueTask<StorageReservation> ReservePathAsync(string? preferredPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var relativePath = string.IsNullOrWhiteSpace(preferredPath)
            ? BuildRelativePath()
            : NormalizePath(preferredPath!.Trim());

        var storagePath = StoragePath.From(relativePath);
        var physicalPath = ResolvePath(storagePath.Value);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        return ValueTask.FromResult(new StorageReservation(_provider, storagePath));
    }

    public ValueTask<Stream> OpenWriteAsync(StorageReservation reservation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        cancellationToken.ThrowIfCancellationRequested();

        var physicalPath = ResolvePath(reservation.Path.Value);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        Stream stream = new FileStream(
            physicalPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        return ValueTask.FromResult(stream);
    }

    public ValueTask<StorageResult> CommitAsync(
        StorageReservation reservation,
        StorageCommitContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var hash = FileHash.From(context.Sha256);
        var snapshot = CreateSnapshot(reservation.Path.Value, hash, context.Length);
        return ValueTask.FromResult(snapshot);
    }

    public async Task<StorageResult> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        var reservation = await ReservePathAsync(preferredPath: null, cancellationToken).ConfigureAwait(false);
        await using var destination = await OpenWriteAsync(reservation, cancellationToken).ConfigureAwait(false);
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long totalBytes = 0;

        try
        {
            while (true)
            {
                var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                incrementalHash.AppendData(buffer, 0, read);
                totalBytes += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

        var hashBytes = incrementalHash.GetHashAndReset();
        var hash = FileHash.From(Convert.ToHexString(hashBytes));
        var commitContext = new StorageCommitContext(totalBytes, hash.Value, Sha1: null);
        var snapshot = await CommitAsync(reservation, commitContext, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public Task<StoragePath> MoveAsync(StoragePath from, StoragePath to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        cancellationToken.ThrowIfCancellationRequested();

        var sourcePath = ResolvePath(from.Value);
        var destinationPath = ResolvePath(to.Value);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        File.Move(sourcePath, destinationPath, overwrite: false);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(StoragePath.From(NormalizePath(to.Value)));
    }

    public async Task<FileStat> StatAsync(StoragePath path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        cancellationToken.ThrowIfCancellationRequested();

        var physicalPath = ResolvePath(path.Value);
        if (!File.Exists(physicalPath))
        {
            throw new FileNotFoundException($"File '{path.Value}' was not found in storage.", physicalPath);
        }

        await using var source = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long totalBytes = 0;

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                incrementalHash.AppendData(buffer, 0, read);
                totalBytes += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var hashBytes = incrementalHash.GetHashAndReset();
        var hash = FileHash.From(Convert.ToHexString(hashBytes));
        var snapshot = CreateSnapshot(path.Value, hash, totalBytes);
        return snapshot.ToFileStat();
    }

    private StorageResult CreateSnapshot(string relativePath, FileHash hash, long length)
    {
        var physicalPath = ResolvePath(relativePath);
        var info = new FileInfo(physicalPath);

        if (!info.Exists)
        {
            throw new FileNotFoundException($"File '{relativePath}' was not found in storage.", physicalPath);
        }

        var attributes = MapAttributes(info.Attributes);
        var normalizedPath = NormalizePath(relativePath);
        var mime = MimeType.From(DefaultMimeType);

        return new StorageResult(
            _provider,
            StoragePath.From(normalizedPath),
            hash,
            ByteSize.From(length),
            mime,
            attributes,
            OwnerSid: null,
            IsEncrypted: attributes.HasFlag(FileAttributesFlags.Encrypted),
            UtcTimestamp.From(info.CreationTimeUtc),
            UtcTimestamp.From(info.LastWriteTimeUtc),
            UtcTimestamp.From(info.LastAccessTimeUtc));
    }

    private static string BuildRelativePath()
    {
        var identifier = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        return Path.Combine(identifier[..2], identifier[2..]);
    }

    private string ResolvePath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var rootFullPath = Path.GetFullPath(_rootPath);
        var trimmedRelative = relativePath.Trim();
        if (trimmedRelative.Length == 0)
        {
            throw new ArgumentException("Relative storage path cannot be empty or whitespace.", nameof(relativePath));
        }

        var sanitizedRelative = trimmedRelative
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(rootFullPath, sanitizedRelative));

        EnsureWithinRoot(rootFullPath, fullPath);

        return fullPath;
    }

    private static void EnsureWithinRoot(string rootFullPath, string fullPath)
    {
        if (!fullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new StoragePathViolationException(rootFullPath, fullPath);
        }

        if (Path.EndsInDirectorySeparator(rootFullPath))
        {
            return;
        }

        var normalizedRoot = TrimTrailingSeparators(rootFullPath);
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new StoragePathViolationException(rootFullPath, fullPath);
        }

        if (fullPath.Length > normalizedRoot.Length)
        {
            var nextChar = fullPath[normalizedRoot.Length];
            if (!IsDirectorySeparator(nextChar))
            {
                throw new StoragePathViolationException(rootFullPath, fullPath);
            }
        }

        var relative = Path.GetRelativePath(rootFullPath, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new StoragePathViolationException(rootFullPath, fullPath);
        }
    }

    private static string TrimTrailingSeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? path : trimmed;
    }

    private static bool IsDirectorySeparator(char character)
        => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar;

    private static string NormalizePath(string relativePath)
    {
        return relativePath.Replace("\\", "/", StringComparison.Ordinal);
    }

    private static FileAttributesFlags MapAttributes(FileAttributes attributes)
    {
        FileAttributesFlags flags = FileAttributesFlags.None;

        if (attributes.HasFlag(FileAttributes.ReadOnly))
        {
            flags |= FileAttributesFlags.ReadOnly;
        }

        if (attributes.HasFlag(FileAttributes.Hidden))
        {
            flags |= FileAttributesFlags.Hidden;
        }

        if (attributes.HasFlag(FileAttributes.System))
        {
            flags |= FileAttributesFlags.System;
        }

        if (attributes.HasFlag(FileAttributes.Directory))
        {
            flags |= FileAttributesFlags.Directory;
        }

        if (attributes.HasFlag(FileAttributes.Archive))
        {
            flags |= FileAttributesFlags.Archive;
        }

        if (attributes.HasFlag(FileAttributes.Device))
        {
            flags |= FileAttributesFlags.Device;
        }

        if (attributes.HasFlag(FileAttributes.Temporary))
        {
            flags |= FileAttributesFlags.Temporary;
        }

        if (attributes.HasFlag(FileAttributes.SparseFile))
        {
            flags |= FileAttributesFlags.SparseFile;
        }

        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            flags |= FileAttributesFlags.ReparsePoint;
        }

        if (attributes.HasFlag(FileAttributes.Compressed))
        {
            flags |= FileAttributesFlags.Compressed;
        }

        if (attributes.HasFlag(FileAttributes.Offline))
        {
            flags |= FileAttributesFlags.Offline;
        }

        if (attributes.HasFlag(FileAttributes.NotContentIndexed))
        {
            flags |= FileAttributesFlags.NotContentIndexed;
        }

        if (attributes.HasFlag(FileAttributes.Encrypted))
        {
            flags |= FileAttributesFlags.Encrypted;
        }

        if (attributes.HasFlag(FileAttributes.IntegrityStream))
        {
            flags |= FileAttributesFlags.IntegrityStream;
        }

        if (attributes.HasFlag(FileAttributes.NoScrubData))
        {
            flags |= FileAttributesFlags.NoScrubData;
        }

        if (flags == FileAttributesFlags.None)
        {
            flags = FileAttributesFlags.Normal;
        }

        return flags;
    }
}
