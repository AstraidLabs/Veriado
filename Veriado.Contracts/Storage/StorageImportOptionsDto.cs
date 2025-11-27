// File: Veriado.Contracts/Storage/StorageImportOptionsDto.cs
namespace Veriado.Contracts.Storage;

/// <summary>
/// Options controlling import behaviour.
/// </summary>
public sealed record StorageImportOptionsDto
{
    /// <summary>Gets a value indicating whether existing database and files may be overwritten.</summary>
    public bool OverwriteExisting { get; init; }

    public StorageVerificationOptionsDto Verification { get; init; } = new();
}
