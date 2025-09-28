namespace Veriado.Domain.Metadata;

/// <summary>
/// Represents file attribute flags similar to those available on Windows file systems.
/// </summary>
[Flags]
public enum FileAttributesFlags
{
    /// <summary>
    /// No special attributes.
    /// </summary>
    None = 0,

    /// <summary>
    /// The file is read-only.
    /// </summary>
    ReadOnly = 1 << 0,

    /// <summary>
    /// The file is hidden.
    /// </summary>
    Hidden = 1 << 1,

    /// <summary>
    /// The file is used by the operating system.
    /// </summary>
    System = 1 << 2,

    /// <summary>
    /// The file is a directory.
    /// </summary>
    Directory = 1 << 3,

    /// <summary>
    /// The file should be archived.
    /// </summary>
    Archive = 1 << 4,

    /// <summary>
    /// The file is a device file.
    /// </summary>
    Device = 1 << 5,

    /// <summary>
    /// The file has no other attributes set.
    /// </summary>
    Normal = 1 << 6,

    /// <summary>
    /// The file is temporary.
    /// </summary>
    Temporary = 1 << 7,

    /// <summary>
    /// The file is sparse.
    /// </summary>
    SparseFile = 1 << 8,

    /// <summary>
    /// The file or directory contains a reparse point.
    /// </summary>
    ReparsePoint = 1 << 9,

    /// <summary>
    /// The file or directory is compressed.
    /// </summary>
    Compressed = 1 << 10,

    /// <summary>
    /// The file is offline.
    /// </summary>
    Offline = 1 << 11,

    /// <summary>
    /// The file is not indexed by the content indexer.
    /// </summary>
    NotContentIndexed = 1 << 12,

    /// <summary>
    /// The file is encrypted.
    /// </summary>
    Encrypted = 1 << 13,

    /// <summary>
    /// Integrity stream is enabled for the file.
    /// </summary>
    IntegrityStream = 1 << 14,

    /// <summary>
    /// The file data is not scrubbed.
    /// </summary>
    NoScrubData = 1 << 15,
}
