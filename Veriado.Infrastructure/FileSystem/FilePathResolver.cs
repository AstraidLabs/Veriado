using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Domain.FileSystem;
using Veriado.Infrastructure.Storage;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Entities;

namespace Veriado.Infrastructure.FileSystem;

/// <summary>
/// Resolves full storage paths by combining the persisted storage root with relative paths.
/// </summary>
public sealed class FilePathResolver : IFilePathResolver
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<FilePathResolver> _logger;
    private string? _cachedRoot;

    public FilePathResolver(AppDbContext dbContext, ILogger<FilePathResolver> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string GetStorageRoot()
    {
        if (!string.IsNullOrWhiteSpace(_cachedRoot))
        {
            return _cachedRoot!;
        }

        try
        {
            var root = _dbContext.StorageRoots.AsNoTracking().SingleOrDefault();
            if (root is null || string.IsNullOrWhiteSpace(root.RootPath))
            {
                const string message = "Storage root is not configured. Run initialisation to set the root path.";
                _logger.LogError("{Message}", message);
                throw new InvalidOperationException(message);
            }

            _cachedRoot = NormalizeRoot(root, _logger);
            return _cachedRoot!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve storage root from persistence.");
            throw;
        }
    }

    public void InvalidateRootCache()
    {
        _cachedRoot = null;
    }

    public string GetFullPath(string relativePath)
        => GetFullPath(relativePath, null);

    public string GetFullPath(string relativePath, string? rootOverride)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path must be provided.", nameof(relativePath));
        }

        try
        {
            var root = string.IsNullOrWhiteSpace(rootOverride)
                ? GetStorageRoot()
                : StorageRootValidator.ValidateWritableRoot(rootOverride!, _logger);

            var normalizedRelative = NormalizeRelative(relativePath, _logger);
            var fullPath = Path.Combine(root, normalizedRelative);
            return Path.GetFullPath(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve full path for relative path {RelativePath}.", relativePath);
            throw;
        }
    }

    public string GetFullPath(FileSystemEntity file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return GetFullPath(file.RelativePath.Value);
    }

    public string GetRelativePath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException("Full path must be provided.", nameof(fullPath));
        }

        try
        {
            var storageRoot = Path.GetFullPath(GetStorageRoot());
            var rootedFullPath = fullPath;
            if (!Path.IsPathRooted(fullPath))
            {
                rootedFullPath = Path.Combine(storageRoot, fullPath);
            }

            var normalizedFull = Path.GetFullPath(rootedFullPath);
            var relative = Path.GetRelativePath(storageRoot, normalizedFull);

            if (IsOutsideRoot(relative))
            {
                _logger.LogWarning(
                    "Full path {FullPath} is not located under storage root {RootPath}.",
                    fullPath,
                    storageRoot);
                throw new InvalidOperationException("Full path does not reside under the configured storage root.");
            }

            var normalized = NormalizeRelative(relative, _logger);
            if (IsOutsideRoot(normalized))
            {
                _logger.LogWarning(
                    "Normalized relative path {RelativePath} escapes storage root {RootPath}.",
                    normalized,
                    storageRoot);
                throw new InvalidOperationException("Relative path cannot escape the configured storage root.");
            }

            return normalized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve relative path from full path {FullPath}.", fullPath);
            throw;
        }
    }

    public void OverrideCachedRoot(string normalizedRoot)
    {
        if (string.IsNullOrWhiteSpace(normalizedRoot))
        {
            throw new ArgumentException("Root path cannot be empty.", nameof(normalizedRoot));
        }

        _cachedRoot = Path.GetFullPath(normalizedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeRoot(FileStorageRootEntity root, ILogger logger)
    {
        var normalized = StorageRootValidator.ValidateWritableRoot(root.RootPath, logger);
        return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal static string NormalizeRelative(string relativePath, ILogger logger)
        => SafePathUtilities.NormalizeRelative(relativePath, logger);

    private static bool IsOutsideRoot(string relative)
        => relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative);
}
