using System.IO;

namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a normalized pointer to externally stored file content.
/// </summary>
public sealed class StoragePath : IEquatable<StoragePath>
{
    private const int MaxLength = 2048;

    private StoragePath(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the normalized storage path value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new <see cref="StoragePath"/> from the provided string value.
    /// </summary>
    /// <param name="value">The raw storage path.</param>
    /// <returns>The created value object.</returns>
    public static StoragePath From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Storage path cannot be null or whitespace.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                normalized.Length,
                $"Storage path exceeds the maximum allowed length of {MaxLength} characters.");
        }

        return new StoragePath(normalized);
    }

    /// <summary>
    /// Creates a new <see cref="StoragePath"/> based on a storage root and relative path.
    /// </summary>
    /// <param name="root">The storage root directory.</param>
    /// <param name="relative">The relative path within the storage root.</param>
    /// <returns>The created value object.</returns>
    /// <exception cref="StoragePathViolationException">Thrown when the resolved path escapes the storage root.</exception>
    public static StoragePath From(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Storage root cannot be null or whitespace.", nameof(root));
        }

        if (relative is null)
        {
            throw new ArgumentNullException(nameof(relative));
        }

        var rootFullPath = Path.GetFullPath(root);
        var trimmedRelative = relative.Trim();
        if (trimmedRelative.Length == 0)
        {
            throw new ArgumentException("Relative storage path cannot be empty or whitespace.", nameof(relative));
        }

        var sanitizedRelative = NormalizeToPlatformSeparators(trimmedRelative);
        var combined = Path.Combine(rootFullPath, sanitizedRelative);
        var fullPath = Path.GetFullPath(combined);

        EnsureWithinRoot(rootFullPath, fullPath);

        var relativePath = Path.GetRelativePath(rootFullPath, fullPath);
        var normalized = NormalizeForStorage(relativePath);
        return From(normalized);
    }

    private static void EnsureWithinRoot(string rootFullPath, string fullPath)
    {
        var rootWithSeparator = AppendDirectorySeparator(rootFullPath);
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new StoragePathViolationException(rootFullPath, fullPath);
        }
    }

    private static string AppendDirectorySeparator(string path)
        => Path.EndsInDirectorySeparator(path)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static string NormalizeToPlatformSeparators(string path)
        => path
            .Replace('\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

    private static string NormalizeForStorage(string path)
        => path.Replace('\', '/');

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <inheritdoc />
    public bool Equals(StoragePath? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is StoragePath other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
}

/// <summary>
/// Represents an attempt to resolve a storage path outside of the configured storage root.
/// </summary>
public sealed class StoragePathViolationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StoragePathViolationException"/> class.
    /// </summary>
    /// <param name="root">The storage root directory.</param>
    /// <param name="attemptedPath">The attempted resolved path.</param>
    public StoragePathViolationException(string root, string attemptedPath)
        : base($"Storage path '{attemptedPath}' resolves outside of storage root '{root}'.")
    {
        Root = root;
        AttemptedPath = attemptedPath;
    }

    /// <summary>
    /// Gets the storage root directory involved in the violation.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Gets the attempted resolved path that triggered the violation.
    /// </summary>
    public string AttemptedPath { get; }
}
