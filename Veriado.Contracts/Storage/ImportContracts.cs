using System;
using System.Collections.Generic;
using Veriado.Appl.Abstractions;

namespace Veriado.Contracts.Storage;

public sealed record ImportRequestDto
{
    public string PackagePath { get; init; } = string.Empty;
    public string? ScopeFilter { get; init; }
        = null;
    public string? TargetStorageRoot { get; init; }
        = null;
    public ImportConflictStrategy? DefaultConflictStrategy { get; init; }
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
    public int UpdatableItems { get; init; }
    public int SkippedItems { get; init; }
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
    public DateTimeOffset LastModifiedAtUtc { get; init; }
}

public sealed record ImportCommitResultDto
{
    public ImportCommitStatus Status { get; init; }
    public int ImportedFiles { get; init; }
    public int SkippedFiles { get; init; }
    public int ConflictedFiles { get; init; }
    public IReadOnlyList<ImportValidationIssueDto> Issues { get; init; } = Array.Empty<ImportValidationIssueDto>();
    public IReadOnlyList<ImportItemPreviewDto> Items { get; init; } = Array.Empty<ImportItemPreviewDto>();
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
    public string? ConflictReason { get; init; }
}
