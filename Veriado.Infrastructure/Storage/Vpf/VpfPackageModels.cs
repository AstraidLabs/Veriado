using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using VtpPackageInfo = Veriado.Application.Abstractions.VtpPackageInfo;

namespace Veriado.Infrastructure.Storage.Vpf;

/// <summary>
/// Root manifest describing the portable package for user-facing presentation.
/// </summary>
public sealed record VpfPackageManifest
{
    public const string ExpectedSpec = "Veriado.Package";
    public const string ExpectedSpecVersion = "1.0";

    [JsonPropertyName("spec")]
    public string Spec { get; init; } = ExpectedSpec;

    [JsonPropertyName("specVersion")]
    public string SpecVersion { get; init; } = ExpectedSpecVersion;

    [JsonPropertyName("packageId")]
    public Guid PackageId { get; init; }
        = Guid.NewGuid();

    [JsonPropertyName("name")]
    public string? Name { get; init; }
        = "Veriado Package";

    [JsonPropertyName("description")]
    public string? Description { get; init; }
        = string.Empty;

    [JsonPropertyName("vtp")]
    public VtpPackageInfo Vtp { get; init; } = new();

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }
        = DateTimeOffset.UtcNow;

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }
        = Environment.UserName;

    [JsonPropertyName("sourceInstanceId")]
    public Guid SourceInstanceId { get; init; }
        = Guid.Empty;

    [JsonPropertyName("sourceInstanceName")]
    public string? SourceInstanceName { get; init; }
        = string.Empty;

    [JsonPropertyName("exportMode")]
    public string ExportMode { get; init; } = "LogicalPerFile";
}

/// <summary>
/// Technical metadata describing application and schema versions captured during export.
/// </summary>
public sealed record VpfTechnicalMetadata
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; } = 1;

    [JsonPropertyName("applicationVersion")]
    public string ApplicationVersion { get; init; } = string.Empty;

    [JsonPropertyName("databaseSchemaVersion")]
    public string? DatabaseSchemaVersion { get; init; }
        = string.Empty;

    [JsonPropertyName("vtp")]
    public VtpPackageInfo Vtp { get; init; } = new();

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
    public int FileDescriptorSchemaVersion { get; init; } = 1;

    [JsonPropertyName("extensions")]
    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Descriptor persisted next to every file inside <c>files/</c>.
/// </summary>
public sealed record VpfFileDescriptor
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = "Veriado.FileDescriptor";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("fileId")]
    public Guid FileId { get; init; }
        = Guid.Empty;

    [JsonPropertyName("originalInstanceId")]
    public Guid OriginalInstanceId { get; init; }
        = Guid.Empty;

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; init; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("contentHash")]
    public string ContentHash { get; init; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }
        = 0;

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }
        = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }
        = DateTimeOffset.MinValue;

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }
        = string.Empty;

    [JsonPropertyName("lastModifiedAtUtc")]
    public DateTimeOffset LastModifiedAtUtc { get; init; }
        = DateTimeOffset.MinValue;

    [JsonPropertyName("lastModifiedBy")]
    public string? LastModifiedBy { get; init; }
        = string.Empty;

    [JsonPropertyName("isReadOnly")]
    public bool IsReadOnly { get; init; }
        = false;

    [JsonPropertyName("labels")]
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();

    [JsonPropertyName("customMetadata")]
    public IDictionary<string, object?> CustomMetadata { get; init; }
        = new Dictionary<string, object?>();

    [JsonPropertyName("extensions")]
    public IDictionary<string, object?> Extensions { get; init; }
        = new Dictionary<string, object?>();
}

public static class VpfPackagePaths
{
    public const string PackageManifestFile = "package.json";
    public const string MetadataFile = "metadata.json";
    public const string FilesDirectory = "files";
    public const string ExtraDirectory = "extra";
}

public static class VpfSerialization
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
