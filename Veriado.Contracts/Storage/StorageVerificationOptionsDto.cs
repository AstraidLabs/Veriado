// File: Veriado.Contracts/Storage/StorageVerificationOptionsDto.cs
namespace Veriado.Contracts.Storage;

/// <summary>
/// Options controlling verification of storage files and database.
/// </summary>
public sealed record StorageVerificationOptionsDto
{
    public bool VerifyFilesBySize { get; init; } = true;

    public bool VerifyFilesByHash { get; init; }
        = false;

    public bool VerifyDatabaseHash { get; init; } = true;
}
