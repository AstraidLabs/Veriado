namespace Veriado.Domain.Files;

/// <summary>
/// Represents lightweight metadata describing externally stored file content.
/// </summary>
public sealed class FileContentEntity
{
    private FileContentEntity(ByteSize length, FileHash hash)
    {
        Hash = hash;
        Length = length;
    }

    /// <summary>
    /// Gets the SHA-256 hash of the content.
    /// </summary>
    public FileHash Hash { get; }

    /// <summary>
    /// Gets the length of the content in bytes.
    /// </summary>
    public ByteSize Length { get; }

    /// <summary>
    /// Creates a <see cref="FileContentEntity"/> from the provided metadata.
    /// </summary>
    /// <param name="hash">The SHA-256 hash of the stored content.</param>
    /// <param name="length">The length of the stored content.</param>
    /// <returns>The created content metadata entity.</returns>
    public static FileContentEntity FromMetadata(FileHash hash, ByteSize length)
    {
        return new FileContentEntity(length, hash);
    }
}
