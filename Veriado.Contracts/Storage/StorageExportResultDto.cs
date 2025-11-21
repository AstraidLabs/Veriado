// File: Veriado.Contracts/Storage/StorageExportResultDto.cs
namespace Veriado.Contracts.Storage;

/// <summary>
/// Represents the outcome of an export operation.
/// </summary>
public sealed record StorageExportResultDto(
    string PackageRoot,
    string DatabasePath,
    int ExportedFiles,
    int MissingFiles);
