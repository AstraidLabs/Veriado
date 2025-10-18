using System;
using System.IO;

namespace Veriado.Infrastructure.Persistence.Connections;

/// <summary>
/// Resolves the absolute path to the SQLite database for runtime and design-time scenarios.
/// </summary>
public sealed class SqlitePathResolver
{
    private const string DefaultFileName = "veriado.db";
    private const string RuntimeDirectoryName = "Veriado";
    private const string DesignDirectoryName = "Veriado.DesignTime";

    private readonly string? _configuredPath;
    private readonly string? _designTimeOverride;

    public SqlitePathResolver(string? configuredPath, string? designTimeOverride = null)
    {
        _configuredPath = configuredPath;
        _designTimeOverride = designTimeOverride;
    }

    /// <summary>
    /// Resolves the database path for the supplied scenario.
    /// </summary>
    /// <param name="scenario">The resolution scenario.</param>
    /// <returns>The absolute database path.</returns>
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
            return NormalizeAndEnsure(basePath);
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, "veriado-data");
        }

        var folder = scenario == SqliteResolutionScenario.DesignTime
            ? Path.Combine(root, DesignDirectoryName)
            : Path.Combine(root, RuntimeDirectoryName);

        return NormalizeAndEnsure(Path.Combine(folder, DefaultFileName));
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
