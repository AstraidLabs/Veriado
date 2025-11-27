// File: Veriado.Contracts/Storage/StorageExportOptionsDto.cs
namespace Veriado.Contracts.Storage;

/// <summary>
/// Options controlling export behaviour.
/// </summary>
public sealed record StorageExportOptionsDto
{
    /// <summary>Gets a value indicating whether existing package contents may be overwritten.</summary>
    public bool OverwriteExisting { get; init; }

    /// <summary>Gets a value indicating whether file hashes should be included in the package.</summary>
    public bool IncludeFileHashes { get; init; }

    public StorageVerificationOptionsDto Verification { get; init; } = new();
}
