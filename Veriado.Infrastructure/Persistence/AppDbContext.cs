using System.Data;
using Microsoft.EntityFrameworkCore.Metadata;
using Veriado.Domain.Audit;
using Veriado.Infrastructure.Persistence.Configurations;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Represents the primary EF Core context used for write operations.
/// </summary>
public sealed class AppDbContext : DbContext
{
    private readonly InfrastructureOptions _options;
    private readonly ILogger<AppDbContext> _logger;
    private const string LegacyBaselineMigrationId = "20250927104926_Init";

    public AppDbContext(DbContextOptions<AppDbContext> options, InfrastructureOptions infrastructureOptions, ILogger<AppDbContext> logger)
        : base(options)
    {
        _options = infrastructureOptions;
        _logger = logger;
    }

    public DbSet<FileEntity> Files => Set<FileEntity>();

    public DbSet<FileAuditEntity> FileAudits => Set<FileAuditEntity>();

    public DbSet<FileContentAuditEntity> FileContentAudits => Set<FileContentAuditEntity>();

    public DbSet<FileDocumentValidityAuditEntity> FileValidityAudits => Set<FileDocumentValidityAuditEntity>();

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    public DbSet<SearchHistoryEntryEntity> SearchHistory => Set<SearchHistoryEntryEntity>();

    public DbSet<SearchFavoriteEntity> SearchFavorites => Set<SearchFavoriteEntity>();

    public DbSet<SynonymEntry> Synonyms => Set<SynonymEntry>();

    public DbSet<SuggestionEntry> Suggestions => Set<SuggestionEntry>();

    public DbSet<DocumentLocationEntity> DocumentLocations => Set<DocumentLocationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        using (InfrastructureModel.UseOptions(_options))
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        }

        if (_options.FtsIndexingMode != FtsIndexingMode.Outbox)
        {
            modelBuilder.Entity<OutboxEvent>().ToTable("outbox_events").Metadata.SetIsTableExcludedFromMigrations(true);
        }
    }

    /// <summary>
    /// Runs post-migration maintenance tasks such as PRAGMA optimize.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Database.IsSqlite())
        {
            await Database.ExecuteSqlRawAsync("PRAGMA optimize;", cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task EnsureSqliteMigrationsLockClearedAsync(CancellationToken cancellationToken)
    {
        const string createTableSql = "CREATE TABLE IF NOT EXISTS \"__EFMigrationsLock\"(\n  \"Id\" INTEGER NOT NULL CONSTRAINT \"PK___EFMigrationsLock\" PRIMARY KEY,\n  \"Timestamp\" TEXT NOT NULL\n);";
        const string deleteSql = "DELETE FROM \"__EFMigrationsLock\";";

        await Database.ExecuteSqlRawAsync(createTableSql, cancellationToken).ConfigureAwait(false);
        var cleared = await Database.ExecuteSqlRawAsync(deleteSql, cancellationToken).ConfigureAwait(false);
        if (cleared > 0)
        {
            _logger.LogInformation("Stale EF migrations lock cleared (removed {LockRows} rows).", cleared);
        }
    }

    internal async Task<bool> NeedsSqliteMigrationsHistoryBaselineAsync(CancellationToken cancellationToken)
    {
        if (!Database.IsSqlite())
        {
            return false;
        }

        var connection = Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var historyCommand = connection.CreateCommand();
            historyCommand.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory' LIMIT 1;";
            var historyResult = await historyCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            var hasHistoryTable = historyResult is not null && historyResult != DBNull.Value;

            if (hasHistoryTable)
            {
                using var baselineCommand = connection.CreateCommand();
                baselineCommand.CommandText = "SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @MigrationId LIMIT 1;";

                var migrationIdParameter = baselineCommand.CreateParameter();
                migrationIdParameter.ParameterName = "@MigrationId";
                migrationIdParameter.Value = LegacyBaselineMigrationId;
                baselineCommand.Parameters.Add(migrationIdParameter);

                var baselineResult = await baselineCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                var hasBaselineRow = baselineResult is not null && baselineResult != DBNull.Value;

                if (hasBaselineRow)
                {
                    return false;
                }
            }

            using var coreTableCommand = connection.CreateCommand();
            coreTableCommand.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name IN ('audit_file', 'files') LIMIT 1;";
            var coreTableResult = await coreTableCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return coreTableResult is not null && coreTableResult != DBNull.Value;
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    internal async Task EnsureSqliteMigrationsHistoryBaselinedAsync(CancellationToken cancellationToken)
    {
        if (!Database.IsSqlite())
        {
            return;
        }

        const string createTableSql = "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\n    \"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY,\n    \"ProductVersion\" TEXT NOT NULL\n);";
        var insertBaselineSql = $"INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('{LegacyBaselineMigrationId}', '9.0.9');";

        await Database.ExecuteSqlRawAsync(createTableSql, cancellationToken).ConfigureAwait(false);
        var inserted = await Database.ExecuteSqlRawAsync(insertBaselineSql, cancellationToken).ConfigureAwait(false);
        if (inserted > 0)
        {
            _logger.LogInformation("Baselined EF migrations history with initial migration for legacy SQLite database.");
        }
    }
}
