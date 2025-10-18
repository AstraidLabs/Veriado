using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Veriado.Infrastructure.Persistence.Connections;

/// <summary>
/// Resolves the absolute path to the SQLite database for runtime and design-time scenarios.
/// </summary>
public sealed class SqlitePathResolver : ISqlitePathResolver
{
    private const string DefaultFileName = "veriado.db";
    private const string DefaultDirectoryName = "Veriado";

    private readonly string? _configuredPath;
    private readonly string? _designTimeOverride;
    private readonly ILogger<SqlitePathResolver>? _logger;

    public SqlitePathResolver(string? configuredPath, string? designTimeOverride = null)
        : this(configuredPath, designTimeOverride, logger: null)
    {
    }

    public SqlitePathResolver(
        string? configuredPath,
        string? designTimeOverride,
        ILogger<SqlitePathResolver>? logger)
    {
        _configuredPath = string.IsNullOrWhiteSpace(configuredPath) ? null : configuredPath;
        _designTimeOverride = string.IsNullOrWhiteSpace(designTimeOverride) ? null : designTimeOverride;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Resolve(SqliteResolutionScenario scenario)
    {
        var basePath = scenario switch
        {
            SqliteResolutionScenario.DesignTime when !string.IsNullOrWhiteSpace(_designTimeOverride)
                => _designTimeOverride!,
            _ => _configuredPath,
        };

        if (!string.IsNullOrWhiteSpace(basePath))
        {
            return NormalizeAndEnsure(basePath!);
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, "veriado-data");
        }

        return NormalizeAndEnsure(Path.Combine(root, DefaultDirectoryName, DefaultFileName));
    }

    /// <inheritdoc />
    public void EnsureStorageExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(fullPath))
        {
            using var _ = File.Create(fullPath);
        }

        _logger?.LogDebug("Ensured SQLite storage at {DatabasePath}", fullPath);
    }

    private static string NormalizeAndEnsure(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return full;
    }
}

/// <summary>
/// Represents the scenarios for resolving SQLite database paths.
/// </summary>
public enum SqliteResolutionScenario
{
    Runtime,
    DesignTime,
}
