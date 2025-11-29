// File: Veriado.Contracts/Storage/StorageOperationResultDto.cs
using System;
using System.Collections.Generic;

namespace Veriado.Contracts.Storage;

public sealed record StorageOperationResultDto
{
    public StorageOperationStatusDto Status { get; init; }

    public string? Message { get; init; }

    public VtpPackageInfo? Vtp { get; init; }

    public VtpImportResultCode? VtpResultCode { get; init; }

    public IReadOnlyList<string> MissingFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> FailedFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool DatabaseHashMatched { get; init; }

    public int VerifiedFilesCount { get; init; }

    public int FailedVerificationCount { get; init; }

    public int MissingFilesCount { get; init; }

    public int FailedFilesCount { get; init; }

    public int WarningCount { get; init; }

    public long? RequiredBytes { get; init; }

    public long? AvailableBytes { get; init; }

    public string? PackageRoot { get; init; }

    public string? TargetStorageRoot { get; init; }

    public string? DatabasePath { get; init; }

    public int AffectedFiles { get; init; }

    public IReadOnlyCollection<string> Errors { get; init; } = Array.Empty<string>();
}

public enum StorageOperationStatusDto
{
    Success,
    PartialSuccess,
    Failed,
    InsufficientSpace,
    InvalidPackage,
    SchemaMismatch,
    PendingMigrations,
}
