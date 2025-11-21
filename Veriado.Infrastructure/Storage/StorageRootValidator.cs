using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Veriado.Infrastructure.Storage;

internal static class StorageRootValidator
{
    public static string ValidateWritableRoot(string proposedRoot, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(proposedRoot))
        {
            throw new ArgumentException("Storage root must be provided.", nameof(proposedRoot));
        }

        var trimmed = proposedRoot.Trim();
        string normalized;

        try
        {
            normalized = Path.GetFullPath(trimmed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to normalize storage root path {RootPath}.", trimmed);
            throw new InvalidOperationException("Unable to normalize the requested storage root path.", ex);
        }

        RejectSystemFolders(normalized, logger);
        TryEnsureWritable(normalized, logger);

        logger.LogInformation("Validated storage root {RootPath}.", normalized);
        return normalized;
    }

    private static void RejectSystemFolders(string normalized, ILogger logger)
    {
        try
        {
            var systemRoot = Path.GetDirectoryName(Environment.SystemDirectory.TrimEnd(Path.DirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(systemRoot) && IsSameOrChild(normalized, systemRoot))
            {
                throw new InvalidOperationException("Storage root cannot be placed inside the operating system directory.");
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles) && IsSameOrChild(normalized, programFiles))
            {
                throw new InvalidOperationException("Storage root cannot be placed inside Program Files.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger.LogError(ex, "Failed to validate system folder exclusions for {RootPath}.", normalized);
            throw;
        }
    }

    private static void TryEnsureWritable(string normalized, ILogger logger)
    {
        try
        {
            Directory.CreateDirectory(normalized);
            var tempPath = Path.Combine(normalized, $".veriado-write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, "veriado");
            File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Storage root {RootPath} is not writable or could not be created.", normalized);
            throw new InvalidOperationException("Storage root must be writable and creatable.", ex);
        }
    }

    private static bool IsSameOrChild(string candidate, string parent)
    {
        var normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (normalizedCandidate.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
