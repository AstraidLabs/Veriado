using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Domain.FileSystem;
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

        var root = _dbContext.StorageRoots.AsNoTracking().SingleOrDefault();
        if (root is null || string.IsNullOrWhiteSpace(root.RootPath))
        {
            _logger.LogWarning("Storage root has not been initialised in the database.");
            throw new InvalidOperationException("Storage root is not configured. Run initialisation to set the root path.");
        }

        _cachedRoot = NormalizeRoot(root);
        return _cachedRoot!;
    }

    public string GetFullPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path must be provided.", nameof(relativePath));
        }

        var root = GetStorageRoot();
        var normalizedRelative = NormalizeRelative(relativePath);
        return Path.GetFullPath(Path.Combine(root, normalizedRelative));
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

        var root = GetStorageRoot();
        var normalizedRoot = Path.GetFullPath(root);
        var normalizedFull = Path.GetFullPath(fullPath);
        var relative = Path.GetRelativePath(normalizedRoot, normalizedFull);

        if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.Equals("..", StringComparison.Ordinal))
        {
            _logger.LogError(
                "Full path {FullPath} is not located under storage root {RootPath}.",
                fullPath,
                normalizedRoot);
            throw new InvalidOperationException("Full path does not reside under the configured storage root.");
        }

        return NormalizeRelative(relative);
    }

    private static string NormalizeRoot(FileStorageRootEntity root)
    {
        var normalized = Path.GetFullPath(root.RootPath.Trim());
        return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeRelative(string relativePath)
    {
        return relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
