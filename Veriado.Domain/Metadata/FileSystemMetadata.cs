using System;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Metadata;

/// <summary>
/// Represents the file system specific metadata snapshot for a file.
/// </summary>
public readonly record struct FileSystemMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemMetadata"/> struct.
    /// </summary>
    /// <param name="attributes">The file attribute flags.</param>
    /// <param name="createdUtc">The creation time.</param>
    /// <param name="lastWriteUtc">The last write time.</param>
    /// <param name="lastAccessUtc">The last access time.</param>
    /// <param name="ownerSid">The owner security identifier.</param>
    /// <param name="hardLinkCount">The number of hard links.</param>
    /// <param name="alternateDataStreamCount">The number of alternate data streams.</param>
    public FileSystemMetadata(
        FileAttributesFlags attributes,
        UtcTimestamp createdUtc,
        UtcTimestamp lastWriteUtc,
        UtcTimestamp lastAccessUtc,
        string? ownerSid,
        uint? hardLinkCount,
        uint? alternateDataStreamCount)
    {
        Attributes = attributes;
        CreatedUtc = createdUtc;
        LastWriteUtc = lastWriteUtc;
        LastAccessUtc = lastAccessUtc;
        OwnerSid = NormalizeSid(ownerSid);
        HardLinkCount = hardLinkCount;
        AlternateDataStreamCount = alternateDataStreamCount;
    }

    /// <summary>
    /// Gets the file attribute flags.
    /// </summary>
    public FileAttributesFlags Attributes { get; }

    /// <summary>
    /// Gets the file creation time in UTC.
    /// </summary>
    public UtcTimestamp CreatedUtc { get; }

    /// <summary>
    /// Gets the last write time in UTC.
    /// </summary>
    public UtcTimestamp LastWriteUtc { get; }

    /// <summary>
    /// Gets the last access time in UTC.
    /// </summary>
    public UtcTimestamp LastAccessUtc { get; }

    /// <summary>
    /// Gets the owner security identifier if known.
    /// </summary>
    public string? OwnerSid { get; }

    /// <summary>
    /// Gets the number of hard links if known.
    /// </summary>
    public uint? HardLinkCount { get; }

    /// <summary>
    /// Gets the number of alternate data streams if known.
    /// </summary>
    public uint? AlternateDataStreamCount { get; }

    /// <summary>
    /// Updates the timestamps while keeping other data intact.
    /// </summary>
    /// <param name="createdUtc">Optional creation time.</param>
    /// <param name="lastWriteUtc">Optional write time.</param>
    /// <param name="lastAccessUtc">Optional access time.</param>
    /// <returns>The updated metadata.</returns>
    public FileSystemMetadata UpdateTimes(UtcTimestamp? createdUtc, UtcTimestamp? lastWriteUtc, UtcTimestamp? lastAccessUtc)
    {
        return new FileSystemMetadata(
            Attributes,
            createdUtc ?? CreatedUtc,
            lastWriteUtc ?? LastWriteUtc,
            lastAccessUtc ?? LastAccessUtc,
            OwnerSid,
            HardLinkCount,
            AlternateDataStreamCount);
    }

    /// <summary>
    /// Updates the owner information.
    /// </summary>
    /// <param name="ownerSid">The new owner security identifier.</param>
    /// <returns>The updated metadata.</returns>
    public FileSystemMetadata UpdateOwner(string? ownerSid)
    {
        return new FileSystemMetadata(
            Attributes,
            CreatedUtc,
            LastWriteUtc,
            LastAccessUtc,
            NormalizeSid(ownerSid),
            HardLinkCount,
            AlternateDataStreamCount);
    }

    /// <summary>
    /// Updates the attribute flags.
    /// </summary>
    /// <param name="attributes">The new attribute flags.</param>
    /// <returns>The updated metadata.</returns>
    public FileSystemMetadata UpdateAttributes(FileAttributesFlags attributes)
    {
        return new FileSystemMetadata(
            attributes,
            CreatedUtc,
            LastWriteUtc,
            LastAccessUtc,
            OwnerSid,
            HardLinkCount,
            AlternateDataStreamCount);
    }

    /// <summary>
    /// Updates the hard link and alternate data stream counts.
    /// </summary>
    /// <param name="hardLinkCount">The new hard link count.</param>
    /// <param name="alternateDataStreamCount">The new alternate data stream count.</param>
    /// <returns>The updated metadata.</returns>
    public FileSystemMetadata UpdateCounts(uint? hardLinkCount, uint? alternateDataStreamCount)
    {
        return new FileSystemMetadata(
            Attributes,
            CreatedUtc,
            LastWriteUtc,
            LastAccessUtc,
            OwnerSid,
            hardLinkCount,
            alternateDataStreamCount);
    }

    private static string? NormalizeSid(string? ownerSid)
    {
        if (string.IsNullOrWhiteSpace(ownerSid))
        {
            return null;
        }

        return ownerSid.Trim();
    }
}
