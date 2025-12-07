using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Veriado.Contracts.Storage;

namespace Veriado.Infrastructure.Storage.Vpf;

public sealed record PackageJsonModel
{
    public const string ExpectedSpec = "Veriado.Package";
    public const string ExpectedSpecVersion = "1.0";

    [JsonPropertyName("spec")]
    public string Spec { get; init; } = ExpectedSpec;

    [JsonPropertyName("specVersion")]
    public string SpecVersion { get; init; } = ExpectedSpecVersion;

    [JsonPropertyName("vtp")]
    public Contracts.Storage.VtpPackageInfo Vtp { get; init; } = new();

    [JsonPropertyName("packageId")]
    public Guid PackageId { get; init; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    public string? Name { get; init; }
        = null;

    [JsonPropertyName("description")]
    public string? Description { get; init; }
        = null;

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }
        = null;

    [JsonPropertyName("sourceInstanceId")]
    public string? SourceInstanceId { get; init; }
        = null;

    /// <summary>
    /// Name of the storage root used during export (alias). Defaults to "default" for legacy packages.
    /// </summary>
    [JsonPropertyName("exportStorageRootAlias")]
    public string? ExportStorageRootAlias { get; init; }
        = null;

    /// <summary>
    /// Optional manifest version to allow future evolution without breaking current readers.
    /// </summary>
    [JsonPropertyName("manifestVersion")]
    public int? ManifestVersion { get; init; }
        = null;

    [JsonPropertyName("sourceInstanceName")]
    public string? SourceInstanceName { get; init; }
        = null;

    [JsonPropertyName("exportMode")]
    public string ExportMode { get; init; } = "LogicalPerFile";
}

public sealed record PathMapping
{
    [JsonPropertyName("storageAlias")]
    public string StorageAlias { get; init; } = "default";

    [JsonPropertyName("relativeRoot")]
    public string RelativeRoot { get; init; } = string.Empty;
}

public sealed record MetadataJsonModel
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; } = 1;

    [JsonPropertyName("applicationVersion")]
    public string ApplicationVersion { get; init; } = string.Empty;

    [JsonPropertyName("vtp")]
    public Contracts.Storage.VtpPackageInfo Vtp { get; init; } = new();

    [JsonPropertyName("databaseSchemaVersion")]
    public string? DatabaseSchemaVersion { get; init; }
        = string.Empty;

    [JsonPropertyName("exportMode")]
    public string ExportMode { get; init; } = "LogicalPerFile";

    [JsonPropertyName("originalStorageRootPath")]
    public string OriginalStorageRootPath { get; init; } = string.Empty;

    [JsonPropertyName("totalFilesCount")]
    public int TotalFilesCount { get; init; }
        = 0;

    [JsonPropertyName("totalFilesBytes")]
    public long TotalFilesBytes { get; init; }
        = 0;

    [JsonPropertyName("hashAlgorithm")]
    public string HashAlgorithm { get; init; } = "SHA256";

    [JsonPropertyName("fileDescriptorSchemaVersion")]
    public int FileDescriptorSchemaVersion { get; init; } = 3;

    [JsonPropertyName("extensions")]
    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();

    [JsonPropertyName("pathMappings")]
    public IReadOnlyList<PathMapping>? PathMappings { get; init; } = null;
}

public sealed record ExportedFileDescriptor
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = "Veriado.FileDescriptor";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 3;

    [JsonPropertyName("fileId")]
    public Guid? FileId { get; init; }
        = null;

    [JsonPropertyName("originalInstanceId")]
    public Guid? OriginalInstanceId { get; init; }
        = null;

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; init; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("extension")]
    public string Extension { get; init; } = string.Empty;

    [JsonPropertyName("contentHash")]
    public string ContentHash { get; init; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }
        = 0;

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }
        = string.Empty;

    /// <summary>
    /// Logical storage alias from which the file was exported. Defaults to "default" for legacy packages.
    /// </summary>
    [JsonPropertyName("storageAlias")]
    public string? StorageAlias { get; init; } = "default";

    /// <summary>
    /// Relative path hint that can be re-mapped on import when FileId cannot be reused.
    /// </summary>
    [JsonPropertyName("logicalPathHint")]
    public string? LogicalPathHint { get; init; }
        = null;

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.MinValue;

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }
        = null;

    [JsonPropertyName("lastModifiedAtUtc")]
    public DateTimeOffset LastModifiedAtUtc { get; init; } = DateTimeOffset.MinValue;

    [JsonPropertyName("lastModifiedBy")]
    public string? LastModifiedBy { get; init; }
        = null;

    [JsonPropertyName("isReadOnly")]
    public bool IsReadOnly { get; init; }
        = false;

    [JsonPropertyName("version")]
    public int Version { get; init; }
        = 0;

    [JsonPropertyName("title")]
    public string? Title { get; init; }
        = null;

    [JsonPropertyName("author")]
    public string? Author { get; init; }
        = null;

    [JsonPropertyName("validity")]
    public ExportedFileValidity? Validity { get; init; }
        = null;

    [JsonPropertyName("systemMetadata")]
    public ExportedSystemMetadata? SystemMetadata { get; init; }
        = null;

    [JsonPropertyName("physicalState")]
    public string? PhysicalState { get; init; }
        = null;

    [JsonPropertyName("labels")]
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();

    [JsonPropertyName("customMetadata")]
    public IDictionary<string, object?> CustomMetadata { get; init; }
        = new Dictionary<string, object?>();

    [JsonPropertyName("extensions")]
    public IDictionary<string, object?> Extensions { get; init; }
        = new Dictionary<string, object?>();
}

public sealed record ExportedFileValidity
{
    [JsonPropertyName("issuedAtUtc")]
    public DateTimeOffset IssuedAtUtc { get; init; }
        = DateTimeOffset.MinValue;

    [JsonPropertyName("validUntilUtc")]
    public DateTimeOffset ValidUntilUtc { get; init; }
        = DateTimeOffset.MinValue;

    [JsonPropertyName("hasPhysicalCopy")]
    public bool HasPhysicalCopy { get; init; }
        = false;

    [JsonPropertyName("hasElectronicCopy")]
    public bool HasElectronicCopy { get; init; }
        = false;
}

public sealed record ExportedSystemMetadata
{
    [JsonPropertyName("attributes")]
    public int Attributes { get; init; }
        = 0;

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; init; }
        = DateTimeOffset.MinValue;

    [JsonPropertyName("lastWriteUtc")]
    public DateTimeOffset LastWriteUtc { get; init; }
        = DateTimeOffset.MinValue;

    [JsonPropertyName("lastAccessUtc")]
    public DateTimeOffset LastAccessUtc { get; init; }
        = DateTimeOffset.MinValue;

    [JsonPropertyName("ownerSid")]
    public string? OwnerSid { get; init; }
        = null;

    [JsonPropertyName("hardLinkCount")]
    public int? HardLinkCount { get; init; }
        = null;

    [JsonPropertyName("alternateDataStreamCount")]
    public int? AlternateDataStreamCount { get; init; }
        = null;
}
