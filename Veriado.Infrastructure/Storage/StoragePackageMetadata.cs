using System;

namespace Veriado.Infrastructure.Storage;

/// <summary>
/// Metadata captured for portable storage packages.
/// </summary>
public sealed record StoragePackageMetadata
{
    public const string CurrentFormatVersion = "1.0";

    public string FormatVersion { get; init; } = CurrentFormatVersion;
    public string ApplicationVersion { get; init; } = string.Empty;
    public string? SchemaVersion { get; init; }
    public string OriginalStorageRoot { get; init; } = string.Empty;
    public int FileCount { get; init; }
    public int MissingFiles { get; init; }
    public long TotalSize { get; init; }
    public string DatabaseFileName { get; init; } = string.Empty;
    public string? DatabaseSha256 { get; init; }
    public DateTimeOffset ExportedAtUtc { get; init; }
}
