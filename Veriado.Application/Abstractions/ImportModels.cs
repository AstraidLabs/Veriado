using System;
using System.Collections.Generic;
using Veriado.Contracts.Storage;

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
    DateTimeOffset LastModifiedAtUtc);

public sealed record ImportItemPreview(
    Guid FileId,
    string RelativePath,
    string FileName,
    string ContentHash,
    long SizeBytes,
    DateTimeOffset LastModifiedAtUtc,
    ImportItemStatus Status,
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
    int UpdatableItems,
    int SkippedItems)
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
            0);
}

public sealed record ImportCommitResult(
    ImportCommitStatus Status,
    int ImportedFiles,
    int SkippedFiles,
    int ConflictedFiles,
    IReadOnlyList<ImportValidationIssue> Issues,
    IReadOnlyList<ImportItemPreview> Items);
