// File: Veriado.Contracts/Storage/StorageMigrationOptionsDto.cs
namespace Veriado.Contracts.Storage;

/// <summary>
/// Options controlling storage root migration.
/// </summary>
public sealed record StorageMigrationOptionsDto
{
    /// <summary>Gets a value indicating whether the original files should be deleted after migration.</summary>
    public bool DeleteSourceAfterCopy { get; init; }

    public StorageVerificationOptionsDto Verification { get; init; } = new();
}
