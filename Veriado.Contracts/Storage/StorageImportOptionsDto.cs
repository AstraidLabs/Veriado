// File: Veriado.Contracts/Storage/StorageImportOptionsDto.cs
namespace Veriado.Contracts.Storage;

/// <summary>
/// Options controlling import behaviour.
/// </summary>
public sealed record StorageImportOptionsDto
{
    /// <summary>Gets a value indicating whether existing database and files may be overwritten.</summary>
    public bool OverwriteExisting { get; init; }

    /// <summary>Gets a value indicating whether imported files should be verified after copy.</summary>
    public bool VerifyAfterCopy { get; init; }
}
