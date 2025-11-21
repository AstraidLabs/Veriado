using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Veriado.Infrastructure.Storage;

internal static class SafePathUtilities
{
    public static string NormalizeAndValidateRoot(string proposedRoot, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (string.IsNullOrWhiteSpace(proposedRoot))
        {
            throw new ArgumentException("Storage root must be provided.", nameof(proposedRoot));
        }

        var normalized = StorageRootValidator.ValidateWritableRoot(proposedRoot, logger)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalized;
    }

    public static string NormalizeRelative(string relativePath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path must be provided.", nameof(relativePath));
        }

        var normalized = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (IsOutsideRoot(normalized))
        {
            logger.LogWarning("Relative path {RelativePath} escapes storage root.", relativePath);
            throw new InvalidOperationException("Relative path cannot escape the configured storage root.");
        }

        ValidatePathCharacters(normalized, logger);
        return normalized;
    }

    public static void EnsureDirectoryForFile(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public static bool ArePathsEquivalent(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }

    public static bool IsOutsideRoot(string relativePath)
    {
        return string.IsNullOrWhiteSpace(relativePath)
            || relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Any(part => part == "..")
            || Path.IsPathRooted(relativePath);
    }

    private static void ValidatePathCharacters(string relativePath, ILogger logger)
    {
        var invalidChars = Path.GetInvalidPathChars().Concat(Path.GetInvalidFileNameChars()).Distinct().ToArray();
        if (relativePath.IndexOfAny(invalidChars) >= 0)
        {
            logger.LogWarning("Relative path {RelativePath} contains invalid characters.", relativePath);
            throw new InvalidOperationException("Relative path contains invalid characters.");
        }
    }
}
