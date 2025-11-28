using System;
using System.Collections.Generic;
using Veriado.Application.Abstractions;

namespace Veriado.Contracts.Storage;

public sealed record ImportRequestDto
{
    public string PackagePath { get; init; } = string.Empty;
    public string? ScopeFilter { get; init; }
        = null;
    public string? TargetStorageRoot { get; init; }
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
}

public sealed record ImportCommitResultDto
{
    public ImportCommitStatus Status { get; init; }
    public int ImportedFiles { get; init; }
    public int SkippedFiles { get; init; }
    public int ConflictedFiles { get; init; }
    public IReadOnlyList<ImportValidationIssueDto> Issues { get; init; } = Array.Empty<ImportValidationIssueDto>();
}
