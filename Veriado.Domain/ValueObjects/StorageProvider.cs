// VERIADO REFACTOR
namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Enumerates storage providers that can host external file content.
/// </summary>
public enum StorageProvider
{
    // VERIADO REFACTOR
    Local = 0,

    // VERIADO REFACTOR
    NetworkShare = 1,

    // VERIADO REFACTOR
    S3 = 2,

    // VERIADO REFACTOR
    AzureBlob = 3,

    // VERIADO REFACTOR
    Other = 99,
}
