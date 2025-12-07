using System;
using System.Collections.Generic;

namespace Veriado.Contracts.Storage;

public enum ImportConflictStrategy
{
    SkipIfExists,
    UpdateIfNewer,
    AlwaysOverwrite,
    CreateDuplicate,
}

public enum ImportIssueType
{
    PackageMissing,
    ManifestMissing,
    ManifestUnsupported,
    MetadataMissing,
    MetadataUnsupported,
    FilesRootMissing,
    MissingDescriptor,
    MissingFile,
    SizeMismatch,
    HashMismatch,
    SchemaUnsupported,
    FileCountMismatch,
    FileBytesMismatch,
    ConflictExistingFile,
}

public enum ImportIssueSeverity
{
    Warning,
    Error,
}

public enum ImportItemStatus
{
    New = 0,
    Same = 1,
    Updated = 2,
    NewerInPackage = Updated,
    SkippedOlder = 3,
    OlderInPackage = SkippedOlder,
    Conflict = 4,
    Failed = 5,
}

public enum ImportCommitStatus
{
    Success,
    PartialSuccess,
    Failed,
}

public sealed record ImportRequestDto
{
    public string PackagePath { get; init; } = string.Empty;
    public string? ScopeFilter { get; init; }
        = null;
    public string? TargetStorageRoot { get; init; }
        = null;
    public ImportConflictStrategy? DefaultConflictStrategy { get; init; }
        = null;
    public string? Password { get; init; }
        = null;
}

public sealed record ImportValidationIssueDto
{
    public ImportIssueType Type { get; init; }
    public ImportIssueSeverity Severity { get; init; }
    public string? RelativePath { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record ImportValidationResultDto
{
    public bool IsValid { get; init; }
    public IReadOnlyList<ImportValidationIssueDto> Issues { get; init; } = Array.Empty<ImportValidationIssueDto>();
    public int DiscoveredFiles { get; init; }
    public int DiscoveredDescriptors { get; init; }
    public long TotalBytes { get; init; }
    public IReadOnlyList<ValidatedImportFileDto> ValidatedFiles { get; init; } = Array.Empty<ValidatedImportFileDto>();
    public IReadOnlyList<ImportItemPreviewDto> Items { get; init; } = Array.Empty<ImportItemPreviewDto>();
    public int NewItems { get; init; }
    public int SameItems { get; init; }
    public int NewerItems { get; init; }
    public int OlderItems { get; init; }
    public int ConflictItems { get; init; }
    public VtpPackageInfo? Vtp { get; init; }
}

public sealed record ValidatedImportFileDto
{
    public string RelativePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string DescriptorPath { get; init; } = string.Empty;
    public Guid FileId { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? MimeType { get; init; }
    public string? Extension { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string? CreatedBy { get; init; }
    public DateTimeOffset LastModifiedAtUtc { get; init; }
    public string? LastModifiedBy { get; init; }
    public string StorageAlias { get; init; } = "default";
    public string LogicalPathHint { get; init; } = string.Empty;
    public Guid? OriginalInstanceId { get; init; }
        = null;
    public bool IsReadOnly { get; init; }
    public int Version { get; init; }
    public string? Title { get; init; }
    public string? Author { get; init; }
    public ImportValidityDto? Validity { get; init; }
    public ImportSystemMetadataDto? SystemMetadata { get; init; }
    public string? PhysicalState { get; init; }
}

public sealed record ImportValidityDto
{
    public DateTimeOffset IssuedAtUtc { get; init; }
    public DateTimeOffset ValidUntilUtc { get; init; }
    public bool HasPhysicalCopy { get; init; }
    public bool HasElectronicCopy { get; init; }
}

public sealed record ImportSystemMetadataDto
{
    public int Attributes { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset LastWriteUtc { get; init; }
    public DateTimeOffset LastAccessUtc { get; init; }
    public string? OwnerSid { get; init; }
    public int? HardLinkCount { get; init; }
    public int? AlternateDataStreamCount { get; init; }
}

public sealed record ImportCommitResultDto
{
    public ImportCommitStatus Status { get; init; }
    public int ImportedFiles { get; init; }
    public int SkippedFiles { get; init; }
    public int ConflictedFiles { get; init; }
    public IReadOnlyList<ImportValidationIssueDto> Issues { get; init; } = Array.Empty<ImportValidationIssueDto>();
    public IReadOnlyList<ImportItemPreviewDto> Items { get; init; } = Array.Empty<ImportItemPreviewDto>();
    public VtpImportResultCode VtpResultCode { get; init; }
    public Guid? CorrelationId { get; init; }
    public IReadOnlyDictionary<Guid, Guid>? FileIdMap { get; init; }
}

public sealed record ImportItemPreviewDto
{
    public Guid FileId { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset LastModifiedAtUtc { get; init; }
    public ImportItemStatus Status { get; init; }
    public VtpImportItemStatus VtpStatus { get; init; }
    public string? ConflictReason { get; init; }
}
