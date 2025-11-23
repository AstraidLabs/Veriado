using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.FileSystem;

namespace Veriado.Infrastructure.Storage;

/// <summary>
/// Provides a file-system based implementation of <see cref="IFileStorage"/> rooted at the configured storage root.
/// </summary>
internal sealed class LocalFileStorage : IFileStorage, IStorageWriter
{
    private const string DefaultMimeType = "application/octet-stream";
    private const string DefaultExtension = ".bin";
    private static readonly IReadOnlyDictionary<string, string> MimeToExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["application/msword"] = ".doc",
        ["application/vnd.ms-excel"] = ".xls",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
        ["application/zip"] = ".zip",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["text/plain"] = ".txt",
    };
    private readonly StorageProvider _provider = StorageProvider.Local;
    private readonly IFilePathResolver _pathResolver;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IFilePathResolver pathResolver, ILogger<LocalFileStorage> logger)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask<StorageReservation> ReservePathAsync(string? preferredPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var relativePath = string.IsNullOrWhiteSpace(preferredPath)
            ? BuildTemporaryPath()
            : NormalizePath(preferredPath!.Trim());

        var storagePath = StoragePath.From(relativePath);
        var physicalPath = ResolvePath(storagePath.Value);
        TryCreateDirectory(Path.GetDirectoryName(physicalPath)!);

        return ValueTask.FromResult(new StorageReservation(_provider, storagePath));
    }

    public ValueTask<Stream> OpenWriteAsync(StorageReservation reservation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var physicalPath = ResolvePath(reservation.Path.Value);
            TryCreateDirectory(Path.GetDirectoryName(physicalPath)!);

            Stream stream = new FileStream(
                physicalPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);

            return ValueTask.FromResult(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open write stream for reservation {ReservationPath}.", reservation.Path.Value);
            throw;
        }
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
        var extension = ResolveExtension(context.Extension, context.Mime);
        var targetRelativePath = NormalizePath(BuildRelativePath(hash.Value, extension));
        var currentPhysical = ResolvePath(reservation.Path.Value);
        var targetPhysical = ResolvePath(targetRelativePath);

        if (!string.Equals(currentPhysical, targetPhysical, StringComparison.OrdinalIgnoreCase))
        {
            TryCreateDirectory(Path.GetDirectoryName(targetPhysical)!);
            var tempPath = Path.Combine(Path.GetDirectoryName(targetPhysical)!, $".tmp-{Guid.NewGuid():N}");

            try
            {
                File.Move(currentPhysical, tempPath, overwrite: true);
                File.Move(tempPath, targetPhysical, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to commit storage reservation {ReservationPath} to {TargetPath}.",
                    currentPhysical,
                    targetPhysical);
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to clean up temporary file {TempFile} after commit failure.", tempPath);
                }

                throw;
            }
        }

        var snapshot = CreateSnapshot(targetRelativePath, hash, context.Length, MimeType.From(ResolveMime(context.Mime)));
        return ValueTask.FromResult(snapshot);
    }

    public async Task<StorageResult> SaveAsync(Stream content, StorageSaveOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        var reservation = await ReservePathAsync(preferredPath: null, cancellationToken).ConfigureAwait(false);
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long totalBytes = 0;
        byte[] hashBytes;

        try
        {
            await using (var destination = await OpenWriteAsync(reservation, cancellationToken).ConfigureAwait(false))
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

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            hashBytes = incrementalHash.GetHashAndReset();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var hash = FileHash.From(Convert.ToHexString(hashBytes));
        var resolvedMime = ResolveMime(options);
        var resolvedExtension = ResolveExtension(options);
        var commitContext = new StorageCommitContext(totalBytes, hash.Value, Sha1: null, resolvedExtension, resolvedMime);
        var snapshot = await CommitAsync(reservation, commitContext, cancellationToken).ConfigureAwait(false);
        return snapshot with { Mime = MimeType.From(resolvedMime) };
    }

    public Task<StoragePath> MoveAsync(StoragePath from, StoragePath to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        cancellationToken.ThrowIfCancellationRequested();

        var sourcePath = ResolvePath(from.Value);
        var destinationPath = ResolvePath(to.Value);
        TryCreateDirectory(Path.GetDirectoryName(destinationPath)!);

        try
        {
            File.Move(sourcePath, destinationPath, overwrite: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move storage file from {Source} to {Destination}.", sourcePath, destinationPath);
            throw;
        }
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(StoragePath.From(NormalizePath(to.Value)));
    }

    public async Task<FileStat> StatAsync(StoragePath path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        cancellationToken.ThrowIfCancellationRequested();

        var physicalPath = ResolvePath(path.Value);
        try
        {
            if (!File.Exists(physicalPath))
            {
                throw new FileNotFoundException($"File '{path.Value}' was not found in storage.", physicalPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stat file at {Path}.", physicalPath);
            throw;
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
        var snapshot = CreateSnapshot(path.Value, hash, totalBytes, MimeType.From(DefaultMimeType));
        return snapshot.ToFileStat();
    }

    private StorageResult CreateSnapshot(string relativePath, FileHash hash, long length, MimeType mime)
    {
        var physicalPath = ResolvePath(relativePath);
        var info = new FileInfo(physicalPath);

        if (!info.Exists)
        {
            throw new FileNotFoundException($"File '{relativePath}' was not found in storage.", physicalPath);
        }

        var attributes = MapAttributes(info.Attributes);
        var normalizedPath = NormalizePath(relativePath);
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

    private string ResolveExtension(StorageSaveOptions? options)
    {
        var extension = NormalizeExtension(options?.Extension);
        if (extension != DefaultExtension)
        {
            return extension;
        }

        var originalExtension = NormalizeExtension(Path.GetExtension(options?.OriginalFileName));
        if (originalExtension != DefaultExtension)
        {
            return originalExtension;
        }

        var mimeExtension = MapMimeToExtension(options?.Mime);
        if (!string.IsNullOrWhiteSpace(mimeExtension))
        {
            return NormalizeExtension(mimeExtension);
        }

        return DefaultExtension;
    }

    private string ResolveExtension(string? extension, string? mime)
    {
        var normalized = NormalizeExtension(extension);
        if (normalized != DefaultExtension)
        {
            return normalized;
        }

        var mimeExtension = MapMimeToExtension(mime);
        if (!string.IsNullOrWhiteSpace(mimeExtension))
        {
            return NormalizeExtension(mimeExtension);
        }

        return DefaultExtension;
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return DefaultExtension;
        }

        var trimmed = extension.Trim();
        if (!trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            trimmed = $".{trimmed}";
        }

        return trimmed
            .Replace(Path.DirectorySeparatorChar, '.')
            .Replace(Path.AltDirectorySeparatorChar, '.');
    }

    private static string? MapMimeToExtension(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return null;
        }

        if (MimeToExtension.TryGetValue(mime, out var mapped))
        {
            return mapped;
        }

        return null;
    }

    private static string ResolveMime(StorageSaveOptions? options)
    {
        return string.IsNullOrWhiteSpace(options?.Mime)
            ? DefaultMimeType
            : options!.Mime!;
    }

    private static string ResolveMime(string? mime)
    {
        return string.IsNullOrWhiteSpace(mime) ? DefaultMimeType : mime;
    }

    private static string BuildRelativePath(string sha256, string extension)
    {
        if (sha256.Length < 2)
        {
            return BuildTemporaryPath();
        }

        return Path.Combine(sha256[..2], string.Concat(sha256[2..], extension));
    }

    private static string BuildTemporaryPath()
    {
        var identifier = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        return Path.Combine("tmp", identifier[..2], identifier[2..]);
    }

    private string ResolvePath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var trimmedRelative = relativePath.Trim();
        if (trimmedRelative.Length == 0)
        {
            throw new ArgumentException("Relative storage path cannot be empty or whitespace.", nameof(relativePath));
        }

        var sanitizedRelative = trimmedRelative
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        try
        {
            var fullPath = _pathResolver.GetFullPath(sanitizedRelative);
            EnsureWithinRoot(fullPath);
            return fullPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve storage path for relative path {RelativePath}.", relativePath);
            throw;
        }
    }

    private void EnsureWithinRoot(string fullPath)
    {
        var storageRoot = Path.GetFullPath(_pathResolver.GetStorageRoot());
        var normalizedFullPath = Path.GetFullPath(fullPath);
        var relativeToRoot = Path.GetRelativePath(storageRoot, normalizedFullPath);

        if (relativeToRoot.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeToRoot))
        {
            _logger.LogWarning(
                "Resolved path {FullPath} is outside of storage root {StorageRoot}.",
                normalizedFullPath,
                storageRoot);

            throw new StoragePathViolationException(storageRoot, normalizedFullPath);
        }
    }

    private static string NormalizePath(string relativePath)
    {
        return relativePath.Replace("\\", "/", StringComparison.Ordinal);
    }

    private void TryCreateDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create directory {Directory} for storage operation.", directory);
            throw;
        }
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
