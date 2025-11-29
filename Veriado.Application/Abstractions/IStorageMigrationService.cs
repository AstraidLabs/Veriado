using System;
using System.Collections.Generic;
using Veriado.Contracts.Storage;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Coordinates pausing of background operations that may interfere with critical file operations.
/// </summary>
public interface IOperationalPauseCoordinator
{
    /// <summary>Gets a value indicating whether operations are currently paused.</summary>
    bool IsPaused { get; }

    /// <summary>Pauses cooperating services until <see cref="Resume"/> is called.</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Resumes any paused services.</summary>
    void Resume();

    /// <summary>Await this to ensure execution only continues when not paused.</summary>
    Task WaitIfPausedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Provides operations for migrating the configured storage root.
/// </summary>
public interface IStorageMigrationService
{
    /// <summary>Migrates the storage root to a new location on disk.</summary>
    Task<StorageOperationResult> MigrateStorageRootAsync(
        string newRootPath,
        StorageMigrationOptions? options,
        CancellationToken cancellationToken);
}

/// <summary>Provides operations for exporting the database and managed storage into a portable package.</summary>
public interface IExportPackageService
{
    Task<StorageOperationResult> ExportPackageAsync(
        string packageRoot,
        StorageExportOptions? options,
        CancellationToken cancellationToken);

    Task<StorageOperationResult> ExportPackageAsync(
        ExportRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Provides operations for importing a portable package into the current environment.</summary>
public interface IImportPackageService
{
    Task<StorageOperationResult> ImportPackageAsync(
        string packageRoot,
        string targetStorageRoot,
        StorageImportOptions? options,
        CancellationToken cancellationToken);

    Task<ImportValidationResult> ValidateLogicalPackageAsync(
        ImportRequest request,
        CancellationToken cancellationToken);

    Task<ImportCommitResult> CommitLogicalPackageAsync(
        ImportRequest request,
        ImportConflictStrategy conflictStrategy,
        CancellationToken cancellationToken);

    Task<ImportValidationResult> ValidateImportAsync(
        ImportRequest request,
        CancellationToken cancellationToken);

    Task<ImportCommitResult> CommitImportAsync(
        ImportRequest request,
        ImportConflictStrategy conflictStrategy,
        CancellationToken cancellationToken);
}

/// <summary>Options controlling verification of storage operations.</summary>
public sealed class StorageVerificationOptions
{
    public bool VerifyFilesBySize { get; init; } = true;

    public bool VerifyFilesByHash { get; init; }
        = false;

    public bool VerifyDatabaseHash { get; init; } = true;
}

/// <summary>Options controlling storage root migration.</summary>
public sealed record StorageMigrationOptions
{
    /// <summary>Gets a value indicating whether the original files should be deleted after a successful copy.</summary>
    public bool DeleteSourceAfterCopy { get; init; }

    /// <summary>Verification configuration for migrated files.</summary>
    public StorageVerificationOptions Verification { get; init; } = new();
}

/// <summary>Options controlling export behaviour.</summary>
public sealed record StorageExportOptions
{
    /// <summary>Gets a value indicating whether existing package contents may be overwritten.</summary>
    public bool OverwriteExisting { get; init; }

    /// <summary>Gets a value indicating whether per-file hashes should be computed during export.</summary>
    public bool IncludeFileHashes { get; init; }

    /// <summary>Verification configuration for exported assets.</summary>
    public StorageVerificationOptions Verification { get; init; } = new();

    /// <summary>Defines the logical export mode used for the package.</summary>
    public StorageExportMode ExportMode { get; init; } = StorageExportMode.LogicalPerFile;
}

/// <summary>Options controlling import behaviour.</summary>
public sealed record StorageImportOptions
{
    /// <summary>Gets a value indicating whether existing database and files may be overwritten.</summary>
    public bool OverwriteExisting { get; init; }

    /// <summary>Verification configuration for imported assets.</summary>
    public StorageVerificationOptions Verification { get; init; } = new();
}

public enum StorageOperationStatus
{
    Success,
    PartialSuccess,
    Failed,
    InsufficientSpace,
    InvalidPackage,
    SchemaMismatch,
    PendingMigrations,
}

public sealed record StorageOperationResult
{
    public StorageOperationStatus Status { get; init; }

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
