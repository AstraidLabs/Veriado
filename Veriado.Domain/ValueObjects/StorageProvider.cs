namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Enumerates storage providers that can host external file content.
/// </summary>
public enum StorageProvider
{
    Local = 0,

    NetworkShare = 1,

    S3 = 2,

    AzureBlob = 3,

    Other = 99,
}
