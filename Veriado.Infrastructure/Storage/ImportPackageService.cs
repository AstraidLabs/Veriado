using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Application.Abstractions;
using Veriado.Contracts.Storage;
using Veriado.Infrastructure.FileSystem;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Entities;
using Veriado.Infrastructure.Storage.Vpf;

namespace Veriado.Infrastructure.Storage;

public sealed class ImportPackageService : IImportPackageService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IFileHashCalculator _hashCalculator;
    private readonly IFilePathResolver _pathResolver;
    private readonly IOperationalPauseCoordinator _pauseCoordinator;
    private readonly IStorageSpaceAnalyzer _spaceAnalyzer;
    private readonly ILogger<ImportPackageService> _logger;
    private readonly VpfPackageValidator _vpfValidator;

    public ImportPackageService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IFileHashCalculator hashCalculator,
        IFilePathResolver pathResolver,
        IOperationalPauseCoordinator pauseCoordinator,
        IStorageSpaceAnalyzer spaceAnalyzer,
        ILogger<ImportPackageService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _pauseCoordinator = pauseCoordinator ?? throw new ArgumentNullException(nameof(pauseCoordinator));
        _spaceAnalyzer = spaceAnalyzer ?? throw new ArgumentNullException(nameof(spaceAnalyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _vpfValidator = new VpfPackageValidator(_hashCalculator);
    }

    public async Task<StorageOperationResult> ImportPackageAsync(
        string packageRoot,
        string targetStorageRoot,
        StorageImportOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new StorageImportOptions();

        var request = new ImportRequest
        {
            PackagePath = packageRoot,
            TargetStorageRoot = targetStorageRoot,
            DefaultConflictStrategy = options.OverwriteExisting
                ? ImportConflictStrategy.AlwaysOverwrite
                : ImportConflictStrategy.UpdateIfNewer,
        };

        var validation = await ValidateLogicalPackageAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return new StorageOperationResult
            {
                Status = StorageOperationStatus.InvalidPackage,
                Message = "Package failed validation; see issues for details.",
                PackageRoot = Path.GetFullPath(packageRoot),
                Errors = validation.Issues.Select(i => i.Message).ToArray(),
            };
        }

        // Optional lightweight space check to avoid starting imports that cannot fit on disk.
        var required = validation.TotalBytes;
        var available = await _spaceAnalyzer
            .GetAvailableBytesAsync(targetStorageRoot, cancellationToken)
            .ConfigureAwait(false);
        if (available < required)
        {
            return new StorageOperationResult
            {
                Status = StorageOperationStatus.InsufficientSpace,
                Message = $"Insufficient space for import. Required {required} bytes, available {available} bytes.",
                RequiredBytes = required,
                AvailableBytes = available,
                PackageRoot = Path.GetFullPath(packageRoot),
                TargetStorageRoot = targetStorageRoot,
            };
        }

        var strategy = request.DefaultConflictStrategy ?? ImportConflictStrategy.UpdateIfNewer;
        var commit = await CommitLogicalPackageAsync(request, strategy, cancellationToken).ConfigureAwait(false);

        return new StorageOperationResult
        {
            Status = commit.Status switch
            {
                ImportCommitStatus.Success => StorageOperationStatus.Success,
                ImportCommitStatus.PartialSuccess => StorageOperationStatus.PartialSuccess,
                _ => StorageOperationStatus.Failed,
            },
            PackageRoot = Path.GetFullPath(packageRoot),
            TargetStorageRoot = targetStorageRoot,
            AffectedFiles = commit.ImportedFiles,
            Errors = commit.Issues.Select(i => i.Message).ToArray(),
            Message = commit.Status == ImportCommitStatus.Success
                ? "Import completed."
                : "Import completed with warnings or conflicts.",
        };
    }

    public Task<ImportValidationResult> ValidateLogicalPackageAsync(
        ImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ValidateAndClassifyAsync(request, cancellationToken);
    }

    public async Task<ImportCommitResult> CommitLogicalPackageAsync(
        ImportRequest request,
        ImportConflictStrategy conflictStrategy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await ValidateLogicalPackageAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return new ImportCommitResult(ImportCommitStatus.Failed, 0, 0, 0, validation.Issues, validation.Items);
        }

        var issues = new List<ImportValidationIssue>(validation.Issues);
        var imported = 0;
        var skipped = 0;
        var conflicted = 0;

        var targetRoot = SafePathUtilities.NormalizeAndValidateRoot(
            request.TargetStorageRoot ?? _pathResolver.GetStorageRoot(),
            _logger);

        var filesRoot = Path.Combine(Path.GetFullPath(request.PackagePath), VpfPackagePaths.FilesDirectory);
        var descriptorLookup = validation.ValidatedFiles.ToDictionary(
            f => f.FileId,
            f => f,
            StringComparer.OrdinalIgnoreCase);

        await _pauseCoordinator.PauseAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var item in validation.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!descriptorLookup.TryGetValue(item.FileId, out var validatedFile))
                {
                    issues.Add(new ImportValidationIssue(
                        ImportIssueType.MissingDescriptor,
                        ImportIssueSeverity.Error,
                        item.RelativePath,
                        "Validated descriptor missing during commit."));
                    conflicted++;
                    continue;
                }

                if (!ShouldImport(item.Status, conflictStrategy, issues, item))
                {
                    skipped++;
                    continue;
                }

                var relativePath = CombineRelative(validatedFile.RelativePath, validatedFile.FileName);
                var sourcePath = Path.Combine(filesRoot, relativePath);
                var destinationPath = Path.Combine(targetRoot, SafePathUtilities.NormalizeRelative(relativePath, _logger));

                if (conflictStrategy == ImportConflictStrategy.CreateDuplicate && item.Status != ImportItemStatus.New)
                {
                    destinationPath = GetDuplicatePath(destinationPath, validatedFile.FileId);
                }

                SafePathUtilities.EnsureDirectoryForFile(destinationPath);

                try
                {
                    await AtomicFileOperations.CopyAsync(
                            sourcePath,
                            destinationPath,
                            overwrite: conflictStrategy is ImportConflictStrategy.AlwaysOverwrite
                                or ImportConflictStrategy.UpdateIfNewer,
                            cancellationToken)
                        .ConfigureAwait(false);

                    imported++;
                }
                catch (Exception ex)
                {
                    conflicted++;
                    issues.Add(new ImportValidationIssue(
                        ImportIssueType.ConflictExistingFile,
                        ImportIssueSeverity.Error,
                        relativePath,
                        $"Failed to copy file: {ex.Message}"));
                    _logger.LogError(ex, "Failed to commit import for {RelativePath}", relativePath);
                }
            }
        }
        finally
        {
            _pauseCoordinator.Resume();
        }

        var status = ImportCommitStatus.Success;
        if (issues.Any(i => i.Severity == ImportIssueSeverity.Error))
        {
            status = ImportCommitStatus.Failed;
        }
        else if (conflicted > 0)
        {
            status = ImportCommitStatus.PartialSuccess;
        }

        return new ImportCommitResult(status, imported, skipped, conflicted, issues, validation.Items);
    }

    private async Task<ImportValidationResult> ValidateAndClassifyAsync(
        ImportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _vpfValidator.ValidateAsync(request.PackagePath, cancellationToken).ConfigureAwait(false);
        if (!result.IsValid)
        {
            return result;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var ids = result.ValidatedFiles.Select(v => v.FileId).ToArray();
        var hashes = result.ValidatedFiles.Select(v => v.ContentHash).Distinct().ToArray();

        var existingById = await dbContext.FileSystems
            .AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new
            {
                f.Id,
                Hash = f.Hash.Value,
                RelativePath = f.RelativePath.Value,
                f.LastWriteUtc,
            })
            .ToDictionaryAsync(f => f.Id, cancellationToken)
            .ConfigureAwait(false);

        var existingByHash = await dbContext.FileSystems
            .AsNoTracking()
            .Where(f => hashes.Contains(f.Hash.Value))
            .Select(f => new
            {
                f.Id,
                Hash = f.Hash.Value,
                RelativePath = f.RelativePath.Value,
                f.LastWriteUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingByPath = await dbContext.FileSystems
            .AsNoTracking()
            .Select(f => new
            {
                RelativePath = f.RelativePath.Value,
                Hash = f.Hash.Value,
                f.LastWriteUtc,
            })
            .ToDictionaryAsync(f => f.RelativePath, cancellationToken)
            .ConfigureAwait(false);

        var previews = new List<ImportItemPreview>();
        var newItems = 0;
        var updatable = 0;
        var skipped = 0;

        foreach (var file in result.ValidatedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pathKey = string.IsNullOrWhiteSpace(file.RelativePath)
                ? file.FileName
                : Path.Combine(file.RelativePath, file.FileName).Replace('\\', '/');

            ImportItemStatus status;
            string? reason = null;

            if (existingById.TryGetValue(file.FileId, out var existingByIdEntry))
            {
                status = Classify(existingByIdEntry.LastWriteUtc.ToDateTimeOffset(), existingByIdEntry.Hash, file, out reason);
            }
            else
            {
                var hashMatch = existingByHash.FirstOrDefault(e => string.Equals(e.Hash, file.ContentHash, StringComparison.OrdinalIgnoreCase));
                if (hashMatch is not null)
                {
                    status = Classify(hashMatch.LastWriteUtc.ToDateTimeOffset(), hashMatch.Hash, file, out reason);
                }
                else if (existingByPath.TryGetValue(pathKey, out var pathEntry))
                {
                    status = ImportItemStatus.ConflictOther;
                    reason = "A file exists at the same path with different content.";
                }
                else
                {
                    status = ImportItemStatus.New;
                }
            }

            switch (status)
            {
                case ImportItemStatus.New:
                    newItems++;
                    break;
                case ImportItemStatus.DuplicateOlderInDb:
                    updatable++;
                    break;
                default:
                    skipped++;
                    break;
            }

            previews.Add(new ImportItemPreview(
                file.FileId,
                file.RelativePath,
                file.FileName,
                file.ContentHash,
                file.SizeBytes,
                file.LastModifiedAtUtc,
                status,
                reason));
        }

        return new ImportValidationResult(
            result.IsValid,
            result.Issues,
            result.DiscoveredFiles,
            result.DiscoveredDescriptors,
            result.TotalBytes,
            result.ValidatedFiles,
            previews,
            newItems,
            updatable,
            skipped);
    }

    private static ImportItemStatus Classify(
        DateTimeOffset existingLastWrite,
        string existingHash,
        ValidatedImportFile file,
        out string? reason)
    {
        reason = null;
        var hashEqual = string.Equals(existingHash, file.ContentHash, StringComparison.OrdinalIgnoreCase);

        if (hashEqual && existingLastWrite == file.LastModifiedAtUtc)
        {
            return ImportItemStatus.DuplicateSameVersion;
        }

        if (file.LastModifiedAtUtc > existingLastWrite)
        {
            reason = "Package contains a newer version than the target database.";
            return ImportItemStatus.DuplicateOlderInDb;
        }

        if (file.LastModifiedAtUtc < existingLastWrite)
        {
            reason = "Target database contains a newer version.";
            return ImportItemStatus.DuplicateNewerInDb;
        }

        if (!hashEqual)
        {
            reason = "Content hash differs while timestamps are equal.";
            return ImportItemStatus.ConflictOther;
        }

        return ImportItemStatus.DuplicateSameVersion;
    }

    private static string CombineRelative(string relativePath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return fileName;
        }

        return Path.Combine(relativePath, fileName).Replace('\\', '/');
    }

    private static string GetDuplicatePath(string destinationPath, Guid fileId)
    {
        var directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);
        var deduped = $"{fileName}-{fileId}{extension}";
        return Path.Combine(directory, deduped);
    }

    private static bool ShouldImport(
        ImportItemStatus status,
        ImportConflictStrategy strategy,
        ICollection<ImportValidationIssue> issues,
        ImportItemPreview item)
    {
        switch (status)
        {
            case ImportItemStatus.New:
                return true;
            case ImportItemStatus.DuplicateOlderInDb:
                return strategy is ImportConflictStrategy.UpdateIfNewer
                    or ImportConflictStrategy.AlwaysOverwrite
                    or ImportConflictStrategy.CreateDuplicate;
            case ImportItemStatus.DuplicateSameVersion:
                return strategy == ImportConflictStrategy.AlwaysOverwrite;
            case ImportItemStatus.DuplicateNewerInDb:
                if (strategy == ImportConflictStrategy.AlwaysOverwrite || strategy == ImportConflictStrategy.CreateDuplicate)
                {
                    return true;
                }

                issues.Add(new ImportValidationIssue(
                    ImportIssueType.ConflictExistingFile,
                    ImportIssueSeverity.Warning,
                    item.RelativePath,
                    item.ConflictReason ?? "Newer version exists in database."));
                return false;
            default:
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.ConflictExistingFile,
                    ImportIssueSeverity.Warning,
                    item.RelativePath,
                    item.ConflictReason ?? "Conflicting entry detected."));
                return false;
        }
    }

    private static async Task<StoragePackageMetadata> ReadMetadataAsync(string metadataPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(metadataPath);
        var metadata = await JsonSerializer.DeserializeAsync<StoragePackageMetadata>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (metadata is null)
        {
            throw new InvalidOperationException("Package metadata could not be read.");
        }

        return metadata;
    }
}
