using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.ValueObjects;

namespace Veriado.Appl.Abstractions;

/// <summary>
/// Provides low-level streaming primitives for writing binary content to storage.
/// </summary>
public interface IStorageWriter
{
    /// <summary>
    /// Reserves a storage location for a new blob.
    /// </summary>
    /// <param name="preferredPath">An optional preferred logical path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A reservation that must be used for subsequent operations.</returns>
    ValueTask<StorageReservation> ReservePathAsync(string? preferredPath, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a writable stream targeting the reserved storage path.
    /// </summary>
    /// <param name="reservation">The reservation returned by <see cref="ReservePathAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A writable stream that should be disposed after use.</returns>
    ValueTask<Stream> OpenWriteAsync(StorageReservation reservation, CancellationToken cancellationToken);

    /// <summary>
    /// Finalizes the write operation and materializes a storage metadata snapshot.
    /// </summary>
    /// <param name="reservation">The reservation to finalize.</param>
    /// <param name="context">The commit context describing the written content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The storage metadata describing the stored content.</returns>
    ValueTask<StorageResult> CommitAsync(StorageReservation reservation, StorageCommitContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Represents a reserved logical storage path.
/// </summary>
/// <param name="Provider">The storage provider that will host the content.</param>
/// <param name="Path">The logical storage path.</param>
public sealed record class StorageReservation(StorageProvider Provider, StoragePath Path);

/// <summary>
/// Provides the metadata required to finalize a storage write operation.
/// </summary>
/// <param name="Length">The total number of bytes written.</param>
/// <param name="Sha256">The SHA-256 hash of the written content.</param>
/// <param name="Sha1">The optional SHA-1 hash of the written content.</param>
/// <param name="Extension">The preferred file extension.</param>
/// <param name="Mime">The MIME type of the content.</param>
public sealed record class StorageCommitContext(long Length, string Sha256, string? Sha1, string? Extension = null, string? Mime = null);
