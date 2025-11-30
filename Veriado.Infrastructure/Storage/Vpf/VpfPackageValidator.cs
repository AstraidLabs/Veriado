using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Abstractions;
using Veriado.Contracts.Storage;
using AppVtpPackageInfo = Veriado.Application.Abstractions.VtpPackageInfo;
using AppVtpPayloadType = Veriado.Application.Abstractions.VtpPayloadType;

namespace Veriado.Infrastructure.Storage.Vpf;

/// <summary>
/// Performs structural and integrity validation of a VPF package prior to import.
/// </summary>
public sealed class VpfPackageValidator
{
    private readonly IFileHashCalculator _hashCalculator;

    public VpfPackageValidator(IFileHashCalculator hashCalculator)
    {
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
    }

    public async Task<ImportValidationResult> ValidateAsync(string packageRoot, CancellationToken cancellationToken)
    {
        var issues = new List<ImportValidationIssue>();
        var normalized = Path.GetFullPath(packageRoot);
        var validatedFiles = new List<ValidatedImportFile>();
        if (!Directory.Exists(normalized))
        {
            issues.Add(new ImportValidationIssue(
                ImportIssueType.PackageMissing,
                ImportIssueSeverity.Error,
                null,
                $"Package directory '{normalized}' does not exist."));

            return ImportValidationResult.FromIssues(issues);
        }

        var manifestPath = Path.Combine(normalized, VpfPackagePaths.PackageManifestFile);
        var metadataPath = Path.Combine(normalized, VpfPackagePaths.MetadataFile);
        if (!File.Exists(manifestPath))
        {
            issues.Add(new ImportValidationIssue(
                ImportIssueType.ManifestMissing,
                ImportIssueSeverity.Error,
                null,
                "Package is missing package.json."));
        }

        if (!File.Exists(metadataPath))
        {
            issues.Add(new ImportValidationIssue(
                ImportIssueType.MetadataMissing,
                ImportIssueSeverity.Error,
                null,
                "Package is missing metadata.json."));
        }

        PackageJsonModel? manifest = null;
        MetadataJsonModel? metadata = null;
        AppVtpPackageInfo? vtp = null;

        if (File.Exists(manifestPath))
        {
            manifest = await DeserializeAsync<PackageJsonModel>(manifestPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(manifest.Spec, PackageJsonModel.ExpectedSpec, StringComparison.Ordinal))
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.ManifestUnsupported,
                    ImportIssueSeverity.Error,
                    null,
                    $"Unsupported manifest spec '{manifest.Spec}'."));
            }

            if (!string.Equals(manifest.SpecVersion, PackageJsonModel.ExpectedSpecVersion, StringComparison.Ordinal))
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.ManifestUnsupported,
                ImportIssueSeverity.Error,
                null,
                $"Unsupported manifest specVersion '{manifest.SpecVersion}'."));
            }

            ValidateVtp(manifest.Vtp.ToModel(), "package.json", ImportIssueType.ManifestUnsupported, issues);
        }

        if (File.Exists(metadataPath))
        {
            metadata = await DeserializeAsync<MetadataJsonModel>(metadataPath, cancellationToken).ConfigureAwait(false);
            if (metadata.FormatVersion != 1)
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.MetadataUnsupported,
                    ImportIssueSeverity.Error,
                    null,
                    $"Unsupported metadata format version '{metadata.FormatVersion}'."));
            }

            if (!string.Equals(metadata.HashAlgorithm, "SHA256", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.MetadataUnsupported,
                ImportIssueSeverity.Error,
                null,
                "Only SHA256 hashAlgorithm is supported."));
            }

            ValidateVtp(metadata.Vtp.ToModel(), "metadata.json", ImportIssueType.MetadataUnsupported, issues);
        }

        var manifestVtp = manifest?.Vtp.ToModel();
        var metadataVtp = metadata?.Vtp.ToModel();
        if (manifestVtp is not null && metadataVtp is not null)
        {
            if (!string.Equals(manifestVtp.ProtocolVersion, metadataVtp.ProtocolVersion, StringComparison.Ordinal))
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.MetadataUnsupported,
                    ImportIssueSeverity.Error,
                    null,
                    "VTP protocolVersion mismatch between package.json and metadata.json."));
            }

            if (manifestVtp.PayloadType != metadataVtp.PayloadType)
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.MetadataUnsupported,
                    ImportIssueSeverity.Error,
                    null,
                    "VTP payloadType mismatch between package.json and metadata.json."));
            }
        }

        vtp = manifestVtp ?? metadataVtp;

        var filesRoot = Path.Combine(normalized, VpfPackagePaths.FilesDirectory);
        if (!Directory.Exists(filesRoot))
        {
            issues.Add(new ImportValidationIssue(
                ImportIssueType.FilesRootMissing,
                ImportIssueSeverity.Error,
                null,
                "Package does not contain a files directory."));

            return new ImportValidationResult(false, issues, 0, 0, 0, validatedFiles, Array.Empty<ImportItemPreview>(), 0, 0, 0, 0, 0, vtp);
        }

        var totalFiles = 0;
        var totalBytes = 0L;
        var descriptorCount = 0;
        var seenDescriptors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue; // descriptors are validated alongside their sibling files
            }

            var relativePath = Path.GetRelativePath(filesRoot, filePath);
            var descriptorPath = filePath + ".json";
            totalFiles++;
            totalBytes += new FileInfo(filePath).Length;

            if (!File.Exists(descriptorPath))
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.MissingDescriptor,
                    ImportIssueSeverity.Error,
                    relativePath,
                    "Descriptor JSON is missing for file."));
                continue;
            }

            descriptorCount++;
            var descriptor = await DeserializeAsync<ExportedFileDescriptor>(descriptorPath, cancellationToken).ConfigureAwait(false);
            seenDescriptors.Add(descriptorPath);

            if (!string.Equals(descriptor.Schema, "Veriado.FileDescriptor", StringComparison.Ordinal))
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.SchemaUnsupported,
                    ImportIssueSeverity.Error,
                    relativePath,
                    $"Unsupported descriptor schema '{descriptor.Schema}'."));
            }

            if (descriptor.SchemaVersion != 1)
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.SchemaUnsupported,
                    ImportIssueSeverity.Error,
                    relativePath,
                    $"Unsupported descriptor schemaVersion '{descriptor.SchemaVersion}'."));
            }

            var size = new FileInfo(filePath).Length;
            if (descriptor.SizeBytes != size)
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.SizeMismatch,
                    ImportIssueSeverity.Error,
                    relativePath,
                    $"Descriptor sizeBytes {descriptor.SizeBytes} does not match file size {size}."));
            }

            if (metadata is not null && metadata.HashAlgorithm.Equals("SHA256", StringComparison.OrdinalIgnoreCase))
            {
                var hash = await _hashCalculator.ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(hash.Value, descriptor.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ImportValidationIssue(
                        ImportIssueType.HashMismatch,
                        ImportIssueSeverity.Error,
                        relativePath,
                        "Content hash does not match descriptor."));
                }
            }

            validatedFiles.Add(new ValidatedImportFile(
                relativePath,
                descriptor.FileName,
                Path.GetRelativePath(normalized, descriptorPath),
                descriptor.FileId,
                descriptor.ContentHash,
                descriptor.SizeBytes,
                descriptor.MimeType,
                descriptor.LastModifiedAtUtc));
        }

        foreach (var descriptorPath in Directory.EnumerateFiles(filesRoot, "*.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (seenDescriptors.Contains(descriptorPath))
            {
                continue;
            }

            var relativeDescriptor = Path.GetRelativePath(filesRoot, descriptorPath);
            var referencedFile = descriptorPath.Substring(0, descriptorPath.Length - 5); // trim .json
            if (!File.Exists(referencedFile))
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.MissingFile,
                    ImportIssueSeverity.Error,
                    relativeDescriptor,
                    "Descriptor is present but the referenced file is missing."));
            }
        }

        if (metadata is not null)
        {
            if (metadata.TotalFilesCount != 0 && metadata.TotalFilesCount != totalFiles)
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.FileCountMismatch,
                    ImportIssueSeverity.Error,
                    null,
                    $"metadata.json totalFilesCount={metadata.TotalFilesCount} differs from detected {totalFiles}."));
            }

            if (metadata.TotalFilesBytes != 0 && metadata.TotalFilesBytes != totalBytes)
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.FileBytesMismatch,
                    ImportIssueSeverity.Error,
                    null,
                    $"metadata.json totalFilesBytes={metadata.TotalFilesBytes} differs from detected {totalBytes}."));
            }

            if (metadata.FileDescriptorSchemaVersion != 1)
            {
                issues.Add(new ImportValidationIssue(
                    ImportIssueType.SchemaUnsupported,
                    ImportIssueSeverity.Error,
                    null,
                    $"Unsupported fileDescriptorSchemaVersion '{metadata.FileDescriptorSchemaVersion}'."));
            }
        }

        return new ImportValidationResult(issues.Count == 0, issues, totalFiles, descriptorCount, totalBytes, validatedFiles, Array.Empty<ImportItemPreview>(), 0, 0, 0, 0, 0, vtp);
    }

    private static void ValidateVtp(
        AppVtpPackageInfo vtp,
        string source,
        ImportIssueType issueType,
        ICollection<ImportValidationIssue> issues)
    {
        if (!string.Equals(vtp.Protocol, "Veriado.Transfer", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ImportValidationIssue(
                issueType,
                ImportIssueSeverity.Error,
                null,
                $"{source} declares unsupported VTP protocol '{vtp.Protocol}'."));
        }

        if (!string.Equals(vtp.ProtocolVersion, "1.0", StringComparison.Ordinal))
        {
            issues.Add(new ImportValidationIssue(
                issueType,
                ImportIssueSeverity.Error,
                null,
                $"{source} declares unsupported VTP protocolVersion '{vtp.ProtocolVersion}'."));
        }

        if (!IsSupportedPayload(vtp.PayloadType))
        {
            issues.Add(new ImportValidationIssue(
                issueType,
                ImportIssueSeverity.Error,
                null,
                $"{source} payloadType '{vtp.PayloadType}' is not supported."));
        }
    }

    private static bool IsSupportedPayload(AppVtpPayloadType payloadType)
        => payloadType is AppVtpPayloadType.VpfPackage
            or AppVtpPayloadType.FullExport
            or AppVtpPayloadType.DeltaExport
            or AppVtpPayloadType.Backup;

    private static async Task<T> DeserializeAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var model = await JsonSerializer.DeserializeAsync<T>(stream, VpfSerialization.Options, cancellationToken)
            .ConfigureAwait(false);

        if (model is null)
        {
            throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} from '{path}'.");
        }

        return model;
    }
}
