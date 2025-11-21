// File: Veriado.Contracts/Storage/StorageImportResultDto.cs
namespace Veriado.Contracts.Storage;

/// <summary>
/// Represents the outcome of a storage import operation.
/// </summary>
public sealed record StorageImportResultDto(
    string PackageRoot,
    string TargetStorageRoot,
    int ImportedFiles,
    int VerificationFailures,
    IReadOnlyCollection<string> Errors);
