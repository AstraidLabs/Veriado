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
            const string message = "Storage root is not configured. Run initialisation to set the root path.";
            _logger.LogError("{Message}", message);
            throw new InvalidOperationException(message);
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
        var fullPath = Path.Combine(root, normalizedRelative);
        return Path.GetFullPath(fullPath);
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

    private static bool IsOutsideRoot(string relative)
        => relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative);
}
