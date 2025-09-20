using System;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Audit;

/// <summary>
/// Represents an auditable snapshot of file content state.
/// </summary>
public sealed class FileContentAuditEntity
{
    private FileContentAuditEntity(
        Guid fileId,
        string action,
        FileHash hash,
        ByteSize size,
        int version,
        DateTimeOffset occurredAtUtc,
        string? actor,
        FileHash? previousHash,
        ByteSize? previousSize)
    {
        FileId = fileId;
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Hash = hash;
        Size = size;
        Version = version;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        Actor = actor;
        PreviousHash = previousHash;
        PreviousSize = previousSize;
    }

    /// <summary>
    /// Gets the affected file identifier.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the action label (e.g. Created, Replaced).
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// Gets the resulting hash.
    /// </summary>
    public FileHash Hash { get; }

    /// <summary>
    /// Gets the resulting size.
    /// </summary>
    public ByteSize Size { get; }

    /// <summary>
    /// Gets the resulting version.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Gets optional actor information.
    /// </summary>
    public string? Actor { get; }

    /// <summary>
    /// Gets the timestamp when the content change occurred (UTC).
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>
    /// Gets the previous hash when applicable.
    /// </summary>
    public FileHash? PreviousHash { get; }

    /// <summary>
    /// Gets the previous size when applicable.
    /// </summary>
    public ByteSize? PreviousSize { get; }

    /// <summary>
    /// Records the initial content snapshot.
    /// </summary>
    public static FileContentAuditEntity Created(Guid fileId, FileHash hash, ByteSize size, int version, DateTimeOffset occurredAtUtc, string? actor = null)
        => new(fileId, "Created", hash, size, version, occurredAtUtc, actor, null, null);

    /// <summary>
    /// Records a content replacement snapshot.
    /// </summary>
    public static FileContentAuditEntity Replaced(Guid fileId, FileHash previousHash, ByteSize previousSize, FileHash newHash, ByteSize newSize, int newVersion, DateTimeOffset occurredAtUtc, string? actor = null)
        => new(fileId, "Replaced", newHash, newSize, newVersion, occurredAtUtc, actor, previousHash, previousSize);
}
