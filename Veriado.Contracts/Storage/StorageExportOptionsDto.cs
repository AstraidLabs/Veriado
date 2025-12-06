// File: Veriado.Contracts/Storage/StorageExportOptionsDto.cs

namespace Veriado.Contracts.Storage;

/// <summary>
/// Defines the logical export mode used for the package.
/// </summary>
public enum StorageExportMode
{
    PhysicalWithDatabase,
    LogicalPerFile,
}

/// <summary>
/// Options controlling export behaviour.
/// </summary>
public sealed record StorageExportOptionsDto
{
    /// <summary>Gets a value indicating whether existing package contents may be overwritten.</summary>
    public bool OverwriteExisting { get; init; }

    /// <summary>Gets a value indicating whether file hashes should be included in the package.</summary>
    public bool IncludeFileHashes { get; init; }
        = true;

    /// <summary>Defines the logical export mode used for the package.</summary>
    public StorageExportMode ExportMode { get; init; }
        = StorageExportMode.LogicalPerFile;

    public StorageVerificationOptionsDto Verification { get; init; } = new();
}
