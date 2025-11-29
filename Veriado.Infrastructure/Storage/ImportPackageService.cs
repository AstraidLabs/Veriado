using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
using Veriado.Infrastructure.Storage.Vpack;

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
    private readonly IVPackContainerService _vpackService;

    public ImportPackageService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IFileHashCalculator hashCalculator,
        IFilePathResolver pathResolver,
        IOperationalPauseCoordinator pauseCoordinator,
        IStorageSpaceAnalyzer spaceAnalyzer,
        ILogger<ImportPackageService> logger,
        IVPackContainerService vpackService)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _pauseCoordinator = pauseCoordinator ?? throw new ArgumentNullException(nameof(pauseCoordinator));
        _spaceAnalyzer = spaceAnalyzer ?? throw new ArgumentNullException(nameof(spaceAnalyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _vpfValidator = new VpfPackageValidator(_hashCalculator);
        _vpackService = vpackService ?? throw new ArgumentNullException(nameof(vpackService));
    }

    public async Task<StorageOperationResult> ImportPackageAsync(
        string packageRoot,
        string targetStorageRoot,
        StorageImportOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new StorageImportOptions();

        var vtp = await TryReadVtpAsync(packageRoot, cancellationToken).ConfigureAwait(false);

        var request = new ImportRequest
        {
            PackagePath = packageRoot,
            TargetStorageRoot = targetStorageRoot,
            DefaultConflictStrategy = options.OverwriteExisting
                ? ImportConflictStrategy.AlwaysOverwrite
                : ImportConflictStrategy.UpdateIfNewer,
        };

        var validation = await ValidateImportAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return new StorageOperationResult
            {
                Status = StorageOperationStatus.InvalidPackage,
                Message = "Package failed validation; see issues for details.",
                PackageRoot = Path.GetFullPath(packageRoot),
                Errors = validation.Issues.Select(i => i.Message).ToArray(),
                Warnings = validation.Issues
                    .Where(i => i.Severity == ImportIssueSeverity.Warning)
                    .Select(i => i.Message)
                    .ToArray(),
                WarningCount = validation.Issues.Count(i => i.Severity == ImportIssueSeverity.Warning),
                MissingFilesCount = validation.Issues.Count(i => i.Type == ImportIssueType.MissingFile),
                FailedFilesCount = validation.Issues.Count(i => i.Severity == ImportIssueSeverity.Error),
                Vtp = vtp,
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
                MissingFilesCount = 0,
                FailedFilesCount = 0,
                WarningCount = 0,
                Warnings = Array.Empty<string>(),
                Vtp = vtp,
            };
        }

        var strategy = request.DefaultConflictStrategy ?? ImportConflictStrategy.UpdateIfNewer;
        var commit = await CommitImportAsync(request, strategy, cancellationToken).ConfigureAwait(false);

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
            Warnings = commit.Issues
                .Where(i => i.Severity == ImportIssueSeverity.Warning)
                .Select(i => i.Message)
                .ToArray(),
            MissingFilesCount = 0,
            FailedFilesCount = commit.ConflictedFiles,
            WarningCount = commit.Issues.Count(i => i.Severity == ImportIssueSeverity.Warning),
            Vtp = vtp,
            Message = commit.Status == ImportCommitStatus.Success
                ? "Import completed."
                : "Import completed with warnings or conflicts.",
        };
    }

    private static async Task<VtpPackageInfo?> TryReadVtpAsync(string packageRoot, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(Path.GetFullPath(packageRoot), VpfPackagePaths.PackageManifestFile);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer
                .DeserializeAsync<PackageJsonModel>(stream, VpfSerialization.Options, cancellationToken)
                .ConfigureAwait(false);
            return manifest?.Vtp;
        }
        catch
        {
            return null;
        }
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
            f => f);

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

    public async Task<ImportValidationResult> ValidateImportAsync(
        ImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.PackagePath))
        {
            return ImportValidationResult.FromIssues(new[]
            {
                new ImportValidationIssue(ImportIssueType.PackageMissing, ImportIssueSeverity.Error, null, "VPack package not found."),
            });
        }

        var (tempRoot, issue) = await ExtractVPackAsync(request.PackagePath, request, cancellationToken).ConfigureAwait(false);
        if (issue is not null)
        {
            return ImportValidationResult.FromIssues(new[] { issue });
        }

        try
        {
            var logicalRequest = request with { PackagePath = tempRoot };
            return await ValidateLogicalPackageAsync(logicalRequest, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CleanupTemp(tempRoot);
        }
    }

    public async Task<ImportCommitResult> CommitImportAsync(
        ImportRequest request,
        ImportConflictStrategy conflictStrategy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (tempRoot, issue) = await ExtractVPackAsync(request.PackagePath, request, cancellationToken).ConfigureAwait(false);
        if (issue is not null)
        {
            var validation = ImportValidationResult.FromIssues(new[] { issue });
            return new ImportCommitResult(ImportCommitStatus.Failed, 0, 0, 0, validation.Issues, Array.Empty<ImportItemPreview>());
        }

        try
        {
            var logicalRequest = request with { PackagePath = tempRoot };
            return await CommitLogicalPackageAsync(logicalRequest, conflictStrategy, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CleanupTemp(tempRoot);
        }
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
            .Select(f => new LocalExistingFile(
                f.Id,
                f.Hash.Value,
                f.RelativePath.Value,
                f.LastWriteUtc.ToDateTimeOffset()))
            .ToDictionaryAsync(f => f.Id, cancellationToken)
            .ConfigureAwait(false);

        var existingByHash = await dbContext.FileSystems
            .AsNoTracking()
            .Where(f => hashes.Contains(f.Hash.Value))
            .Select(f => new LocalExistingFile(
                f.Id,
                f.Hash.Value,
                f.RelativePath.Value,
                f.LastWriteUtc.ToDateTimeOffset()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingByPath = await dbContext.FileSystems
            .AsNoTracking()
            .Select(f => new LocalExistingFile(
                f.Id,
                f.Hash.Value,
                f.RelativePath.Value,
                f.LastWriteUtc.ToDateTimeOffset()))
            .ToDictionaryAsync(f => f.RelativePath, cancellationToken)
            .ConfigureAwait(false);

        var previews = new List<ImportItemPreview>();
        var newItems = 0;
        var sameItems = 0;
        var newerItems = 0;
        var olderItems = 0;
        var conflictItems = 0;

        foreach (var file in result.ValidatedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pathKey = string.IsNullOrWhiteSpace(file.RelativePath)
                ? file.FileName
                : Path.Combine(file.RelativePath, file.FileName).Replace('\\', '/');

            var (status, reason) = Classify(existingById, existingByHash, existingByPath, pathKey, file);

            switch (status)
            {
                case ImportItemStatus.New:
                    newItems++;
                    break;
                case ImportItemStatus.Same:
                    sameItems++;
                    break;
                case ImportItemStatus.NewerInPackage:
                    newerItems++;
                    break;
                case ImportItemStatus.OlderInPackage:
                    olderItems++;
                    break;
                default:
                    conflictItems++;
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
            sameItems,
            newerItems,
            olderItems,
            conflictItems);
    }

    private static (ImportItemStatus Status, string? Reason) Classify(
        IReadOnlyDictionary<Guid, LocalExistingFile> existingById,
        IReadOnlyList<LocalExistingFile> existingByHash,
        IReadOnlyDictionary<string, LocalExistingFile> existingByPath,
        string pathKey,
        ValidatedImportFile file)
    {
        if (existingById.TryGetValue(file.FileId, out var existingByIdEntry))
        {
            return Compare(existingByIdEntry.LastWriteUtc, existingByIdEntry.Hash, file);
        }

        var hashMatch = existingByHash.FirstOrDefault(e => string.Equals(e.Hash, file.ContentHash, StringComparison.OrdinalIgnoreCase));
        if (hashMatch is not null)
        {
            return Compare(hashMatch.LastWriteUtc, hashMatch.Hash, file);
        }

        if (existingByPath.TryGetValue(pathKey, out var pathEntry))
        {
            return (ImportItemStatus.Conflict, "A file exists at the same path with different content.");
        }

        return (ImportItemStatus.New, null);
    }

    private static (ImportItemStatus Status, string? Reason) Compare(
        DateTimeOffset existingLastWrite,
        string existingHash,
        ValidatedImportFile file)
    {
        var hashEqual = string.Equals(existingHash, file.ContentHash, StringComparison.OrdinalIgnoreCase);

        if (hashEqual && existingLastWrite == file.LastModifiedAtUtc)
        {
            return (ImportItemStatus.Same, null);
        }

        if (file.LastModifiedAtUtc > existingLastWrite)
        {
            return (ImportItemStatus.NewerInPackage, "Package contains a newer version.");
        }

        if (file.LastModifiedAtUtc < existingLastWrite)
        {
            return (ImportItemStatus.OlderInPackage, "Target database contains a newer version.");
        }

        if (!hashEqual)
        {
            return (ImportItemStatus.Conflict, "Content hash differs while timestamps are equal.");
        }

        return (ImportItemStatus.Same, null);
    }

    private sealed record LocalExistingFile(
        Guid Id,
        string Hash,
        string RelativePath,
        DateTimeOffset LastWriteUtc);

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

    private async Task<(string TempRoot, ImportValidationIssue? Issue)> ExtractVPackAsync(
        string packagePath,
        ImportRequest request,
        CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"veriado-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        await using var input = File.OpenRead(packagePath);
        var open = await _vpackService.OpenContainerAsync(
                input,
                new VPackOpenOptions { Password = request.Password },
                cancellationToken)
            .ConfigureAwait(false);

        if (!open.Success || open.PayloadStream is null)
        {
            return (tempRoot, new ImportValidationIssue(
                ImportIssueType.MetadataUnsupported,
                ImportIssueSeverity.Error,
                null,
                open.Error ?? "VPack container could not be opened."));
        }

        await using (open.PayloadStream)
        using (var archive = new ZipArchive(open.PayloadStream, ZipArchiveMode.Read, leaveOpen: false))
        {
            archive.ExtractToDirectory(tempRoot);
        }

        return (tempRoot, null);
    }

    private static void CleanupTemp(string tempRoot)
    {
        if (string.IsNullOrWhiteSpace(tempRoot) || !Directory.Exists(tempRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(tempRoot, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
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
            case ImportItemStatus.Same:
                return strategy is ImportConflictStrategy.AlwaysOverwrite or ImportConflictStrategy.CreateDuplicate;
            case ImportItemStatus.NewerInPackage:
                return strategy is ImportConflictStrategy.UpdateIfNewer
                    or ImportConflictStrategy.AlwaysOverwrite
                    or ImportConflictStrategy.CreateDuplicate;
            case ImportItemStatus.OlderInPackage:
                if (strategy is ImportConflictStrategy.AlwaysOverwrite or ImportConflictStrategy.CreateDuplicate)
                {
                    return true;
                }

                issues.Add(new ImportValidationIssue(
                    ImportIssueType.ConflictExistingFile,
                    ImportIssueSeverity.Warning,
                    item.RelativePath,
                    item.ConflictReason ?? "Target has newer version."));
                return false;
            default:
                if (strategy is ImportConflictStrategy.AlwaysOverwrite or ImportConflictStrategy.CreateDuplicate)
                {
                    return true;
                }

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
