using System;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Persistence.Connections;

/// <summary>
/// Provides a deterministic SQLite connection string shared by runtime and design-time infrastructure components.
/// </summary>
public sealed class SqliteConnectionStringProvider : IConnectionStringProvider
{
    private readonly ILogger<SqliteConnectionStringProvider> _logger;
    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteConnectionStringProvider(
        IOptions<InfrastructureOptions> options,
        ILogger<SqliteConnectionStringProvider> logger)
        : this(options.Value, logger)
    {
    }

    public SqliteConnectionStringProvider(InfrastructureOptions options, ILogger<SqliteConnectionStringProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var configuredPath = string.IsNullOrWhiteSpace(options.DbPath) ? null : options.DbPath;
        var designOverride = Environment.GetEnvironmentVariable("VERIADO_DESIGNTIME_DB_PATH");
        var resolver = new SqlitePathResolver(configuredPath, designOverride);

        _databasePath = resolver.Resolve(SqliteResolutionScenario.Runtime);
        _connectionString = BuildConnectionString(_databasePath);

        _logger.LogInformation("Using SQLite DB: {DatabasePath}", _databasePath);
    }

    /// <inheritdoc />
    public string DatabasePath => _databasePath;

    /// <inheritdoc />
    public string ConnectionString => _connectionString;

    /// <inheritdoc />
    public SqliteConnection CreateConnection()
        => new(ConnectionString);

    /// <summary>
    /// Creates a provider configured for EF Core design-time tooling.
    /// </summary>
    /// <param name="options">The infrastructure options.</param>
    /// <param name="logger">The logger to use.</param>
    /// <returns>The connection string provider.</returns>
    public static SqliteConnectionStringProvider CreateDesignTimeProvider(
        InfrastructureOptions options,
        ILogger<SqliteConnectionStringProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var configuredPath = string.IsNullOrWhiteSpace(options.DbPath) ? null : options.DbPath;
        var designOverride = Environment.GetEnvironmentVariable("VERIADO_DESIGNTIME_DB_PATH");
        var resolver = new SqlitePathResolver(configuredPath, designOverride);

        var designPath = resolver.Resolve(SqliteResolutionScenario.DesignTime);
        var connectionString = BuildConnectionString(designPath);

        logger.LogInformation("Using SQLite DB: {DatabasePath} (design-time)", designPath);

        return new SqliteConnectionStringProvider(designPath, connectionString, logger);
    }

    private SqliteConnectionStringProvider(string databasePath, string connectionString, ILogger<SqliteConnectionStringProvider> logger)
    {
        _logger = logger;
        _databasePath = databasePath;
        _connectionString = connectionString;
    }

    private static string BuildConnectionString(string dbPath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Cache = SqliteCacheMode.Shared,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };

        return builder.ConnectionString;
    }
}
