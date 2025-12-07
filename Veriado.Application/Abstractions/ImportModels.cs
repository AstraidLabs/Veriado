using System;
using System.Collections.Generic;
using Veriado.Contracts.Storage;

namespace Veriado.Appl.Abstractions;

/// <summary>
/// Two-phase import request information.
/// </summary>
public sealed record ImportRequest
{
    public string PackagePath { get; init; } = string.Empty;

    /// <summary>Optional filter (tags, subfolder, etc.) reserved for future extension.</summary>
    public string? ScopeFilter { get; init; }
        = null;

    /// <summary>Optional explicit target storage root for commit stage.</summary>
    public string? TargetStorageRoot { get; init; }
        = null;

    /// <summary>Optional default conflict strategy to use for commit when caller does not pass one explicitly.</summary>
    public ImportConflictStrategy? DefaultConflictStrategy { get; init; }
        = null;

    public string? Password { get; init; }
        = null;
}

public sealed record ImportValidationIssue(
    ImportIssueType Type,
    ImportIssueSeverity Severity,
    string? RelativePath,
    string Message);

public sealed record ValidatedImportFile(
    string RelativePath,
    string FileName,
    string DescriptorPath,
    Guid FileId,
    string ContentHash,
    long SizeBytes,
    string? MimeType,
    string? Extension,
    DateTimeOffset CreatedAtUtc,
    string? CreatedBy,
    DateTimeOffset LastModifiedAtUtc,
    string? LastModifiedBy,
    string StorageAlias,
    string LogicalPathHint,
    Guid? OriginalInstanceId,
    bool IsReadOnly,
    int Version,
    string? Title,
    string? Author,
    ImportValidityInfo? Validity,
    ImportSystemMetadataInfo? SystemMetadata,
    string? PhysicalState);

public sealed record ImportValidityInfo(
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ValidUntilUtc,
    bool HasPhysicalCopy,
    bool HasElectronicCopy);

public sealed record ImportSystemMetadataInfo(
    int Attributes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastWriteUtc,
    DateTimeOffset LastAccessUtc,
    string? OwnerSid,
    int? HardLinkCount,
    int? AlternateDataStreamCount);

public sealed record ImportItemPreview(
    Guid FileId,
    string RelativePath,
    string FileName,
    string ContentHash,
    long SizeBytes,
    DateTimeOffset LastModifiedAtUtc,
    ImportItemStatus Status,
    VtpImportItemStatus VtpStatus,
    string? ConflictReason);

public sealed record ImportValidationResult(
    bool IsValid,
    IReadOnlyList<ImportValidationIssue> Issues,
    int DiscoveredFiles,
    int DiscoveredDescriptors,
    long TotalBytes,
    IReadOnlyList<ValidatedImportFile> ValidatedFiles,
    IReadOnlyList<ImportItemPreview> Items,
    int NewItems,
    int SameItems,
    int NewerItems,
    int OlderItems,
    int ConflictItems,
    VtpPackageInfo? Vtp)
{
    public static ImportValidationResult FromIssues(IReadOnlyList<ImportValidationIssue> issues)
        => new(
            issues.Count == 0,
            issues,
            0,
            0,
            0,
            Array.Empty<ValidatedImportFile>(),
            Array.Empty<ImportItemPreview>(),
            0,
            0,
            0,
            0,
            0,
            null);
}

public sealed record ImportCommitResult(
    ImportCommitStatus Status,
    int ImportedFiles,
    int SkippedFiles,
    int ConflictedFiles,
    IReadOnlyList<ImportValidationIssue> Issues,
    IReadOnlyList<ImportItemPreview> Items,
    VtpImportResultCode VtpResultCode,
    Guid? CorrelationId,
    IReadOnlyDictionary<Guid, Guid>? FileIdMap = null);
