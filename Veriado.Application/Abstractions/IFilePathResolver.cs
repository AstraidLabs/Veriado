using Veriado.Domain.FileSystem;

namespace Veriado.Appl.Abstractions;

/// <summary>
/// Resolves logical storage paths to physical file system locations and vice versa.
/// </summary>
public interface IFilePathResolver
{
    /// <summary>
    /// Gets the configured storage root path.
    /// </summary>
    /// <returns>The storage root.</returns>
    string GetStorageRoot();

    /// <summary>
    /// Combines the storage root with the provided relative path.
    /// </summary>
    /// <param name="relativePath">The path relative to the storage root.</param>
    /// <returns>The absolute path.</returns>
    string GetFullPath(string relativePath);

    /// <summary>
    /// Combines the provided root override with the relative path.
    /// </summary>
    /// <param name="relativePath">The path relative to the storage root.</param>
    /// <param name="rootOverride">The optional root to use instead of the configured one.</param>
    /// <returns>The absolute path.</returns>
    string GetFullPath(string relativePath, string? rootOverride);

    /// <summary>
    /// Resolves the file system entity's relative path to the physical path.
    /// </summary>
    /// <param name="file">The file system entity.</param>
    /// <returns>The absolute path.</returns>
    string GetFullPath(FileSystemEntity file);

    /// <summary>
    /// Converts a physical path under the storage root into its relative representation.
    /// </summary>
    /// <param name="fullPath">The absolute path to convert.</param>
    /// <returns>The normalized relative path.</returns>
    string GetRelativePath(string fullPath);
}
