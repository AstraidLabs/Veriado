using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Provides operations for migrating, exporting, and importing storage.
/// </summary>
public interface IStorageMigrationService
{
    /// <summary>
    /// Migrates the storage root to a new location on disk.
    /// </summary>
    /// <param name="newRootPath">The target storage root.</param>
    /// <param name="options">Optional migration options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The migration result.</returns>
    Task<StorageMigrationResult> MigrateStorageRootAsync(
        string newRootPath,
        StorageMigrationOptions? options,
        CancellationToken cancellationToken);

    /// <summary>
    /// Exports the database and storage content into a portable package.
    /// </summary>
    /// <param name="packageRoot">The destination directory for the package.</param>
    /// <param name="options">Optional export options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The export result.</returns>
    Task<StorageExportResult> ExportPackageAsync(
        string packageRoot,
        StorageExportOptions? options,
        CancellationToken cancellationToken);

    /// <summary>
    /// Imports a previously exported package into the current environment.
    /// </summary>
    /// <param name="packageRoot">The root directory of the package.</param>
    /// <param name="targetStorageRoot">The target storage root for imported files.</param>
    /// <param name="options">Optional import options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The import result.</returns>
    Task<StorageImportResult> ImportPackageAsync(
        string packageRoot,
        string targetStorageRoot,
        StorageImportOptions? options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Options controlling storage root migration.
/// </summary>
public sealed record StorageMigrationOptions
{
    /// <summary>
    /// Gets a value indicating whether the original files should be deleted after a successful copy.
    /// </summary>
    public bool DeleteSourceAfterCopy { get; init; }

    /// <summary>
    /// Gets a value indicating whether migrated files should be validated using SHA-256 hashes.
    /// </summary>
    public bool VerifyHashes { get; init; }
}

/// <summary>
/// Options controlling export behaviour.
/// </summary>
public sealed record StorageExportOptions
{
    /// <summary>
    /// Gets a value indicating whether existing package contents may be overwritten.
    /// </summary>
    public bool OverwriteExisting { get; init; }
}

/// <summary>
/// Options controlling import behaviour.
/// </summary>
public sealed record StorageImportOptions
{
    /// <summary>
    /// Gets a value indicating whether existing database and files may be overwritten.
    /// </summary>
    public bool OverwriteExisting { get; init; }

    /// <summary>
    /// Gets a value indicating whether imported files should be validated after copy.
    /// </summary>
    public bool VerifyAfterCopy { get; init; }
}

/// <summary>
/// Result information for storage migrations.
/// </summary>
public sealed record StorageMigrationResult(string OldRoot, string NewRoot)
{
    public int MigratedFiles { get; init; }
    public int MissingSources { get; init; }
    public int VerificationFailures { get; init; }
    public IReadOnlyCollection<string> Errors { get; init; } = new List<string>();
}

/// <summary>
/// Result information for export operations.
/// </summary>
public sealed record StorageExportResult(string PackageRoot)
{
    public string DatabasePath { get; init; } = string.Empty;
    public int ExportedFiles { get; init; }
    public int MissingFiles { get; init; }
}

/// <summary>
/// Result information for import operations.
/// </summary>
public sealed record StorageImportResult(string PackageRoot, string TargetStorageRoot)
{
    public int ImportedFiles { get; init; }
    public int VerificationFailures { get; init; }
    public IReadOnlyCollection<string> Errors { get; init; } = new List<string>();
}
