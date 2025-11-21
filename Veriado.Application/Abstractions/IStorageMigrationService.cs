using System.Collections.Generic;
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
    Task<StorageMigrationResult> MigrateStorageRootAsync(
        string newRootPath,
        StorageMigrationOptions? options,
        CancellationToken cancellationToken);
}

/// <summary>Provides operations for exporting the database and managed storage into a portable package.</summary>
public interface IExportPackageService
{
    Task<StorageExportResult> ExportPackageAsync(
        string packageRoot,
        StorageExportOptions? options,
        CancellationToken cancellationToken);
}

/// <summary>Provides operations for importing a portable package into the current environment.</summary>
public interface IImportPackageService
{
    Task<StorageImportResult> ImportPackageAsync(
        string packageRoot,
        string targetStorageRoot,
        StorageImportOptions? options,
        CancellationToken cancellationToken);
}

/// <summary>Options controlling storage root migration.</summary>
public sealed record StorageMigrationOptions
{
    /// <summary>Gets a value indicating whether the original files should be deleted after a successful copy.</summary>
    public bool DeleteSourceAfterCopy { get; init; }

    /// <summary>Gets a value indicating whether migrated files should be validated using SHA-256 hashes.</summary>
    public bool VerifyHashes { get; init; }
}

/// <summary>Options controlling export behaviour.</summary>
public sealed record StorageExportOptions
{
    /// <summary>Gets a value indicating whether existing package contents may be overwritten.</summary>
    public bool OverwriteExisting { get; init; }
}

/// <summary>Options controlling import behaviour.</summary>
public sealed record StorageImportOptions
{
    /// <summary>Gets a value indicating whether existing database and files may be overwritten.</summary>
    public bool OverwriteExisting { get; init; }

    /// <summary>Gets a value indicating whether imported files should be validated after copy.</summary>
    public bool VerifyAfterCopy { get; init; }
}

/// <summary>Result information for storage migrations.</summary>
public sealed record StorageMigrationResult(string OldRoot, string NewRoot)
{
    public int MigratedFiles { get; init; }
    public int MissingSources { get; init; }
    public int VerificationFailures { get; init; }
    public IReadOnlyCollection<string> Errors { get; init; } = new List<string>();
}

/// <summary>Result information for export operations.</summary>
public sealed record StorageExportResult(string PackageRoot)
{
    public string DatabasePath { get; init; } = string.Empty;
    public int ExportedFiles { get; init; }
    public int MissingFiles { get; init; }
}

/// <summary>Result information for import operations.</summary>
public sealed record StorageImportResult(string PackageRoot, string TargetStorageRoot)
{
    public int ImportedFiles { get; init; }
    public int VerificationFailures { get; init; }
    public IReadOnlyCollection<string> Errors { get; init; } = new List<string>();
}
