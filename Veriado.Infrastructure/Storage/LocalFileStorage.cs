using System;
using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using Veriado.Appl.Abstractions;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Infrastructure.Storage;

/// <summary>
/// Provides a simple file-system based implementation of <see cref="IFileStorage"/> for local development scenarios.
/// </summary>
internal sealed class LocalFileStorage : IFileStorage
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

    public async Task<StorageResult> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        var relativePath = BuildRelativePath();
        var physicalPath = ResolvePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        await using var destination = new FileStream(physicalPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
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
        return CreateSnapshot(relativePath, hash, totalBytes);
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

        var combined = Path.Combine(rootFullPath, sanitizedRelative);
        var fullPath = Path.GetFullPath(combined);
        var rootWithSeparator = Path.EndsInDirectorySeparator(rootFullPath)
            ? rootFullPath
            : rootFullPath + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new StoragePathViolationException(rootFullPath, fullPath);
        }

        return fullPath;
    }

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
