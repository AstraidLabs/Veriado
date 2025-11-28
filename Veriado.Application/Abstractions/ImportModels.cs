using System;
using System.Collections.Generic;

namespace Veriado.Application.Abstractions;

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
}

public enum ImportConflictStrategy
{
    SkipExisting,
    Overwrite,
    Duplicate,
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

public sealed record ImportValidationIssue(
    ImportIssueType Type,
    ImportIssueSeverity Severity,
    string? RelativePath,
    string Message);

public sealed record ImportValidationResult(
    bool IsValid,
    IReadOnlyList<ImportValidationIssue> Issues,
    int DiscoveredFiles,
    int DiscoveredDescriptors,
    long TotalBytes)
{
    public static ImportValidationResult FromIssues(IReadOnlyList<ImportValidationIssue> issues)
        => new(issues.Count == 0, issues, 0, 0, 0);
}

public enum ImportCommitStatus
{
    Success,
    PartialSuccess,
    Failed,
}

public sealed record ImportCommitResult(
    ImportCommitStatus Status,
    int ImportedFiles,
    int SkippedFiles,
    int ConflictedFiles,
    IReadOnlyList<ImportValidationIssue> Issues);
