using System;
using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Metadata;
using Veriado.Domain.Audit;
using Veriado.Infrastructure.Persistence.Configurations;
using Veriado.Infrastructure.Persistence.EventLog;
using Veriado.Infrastructure.Persistence.WriteAhead;

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
        EnsureSqliteProvider();
    }

    public DbSet<FileEntity> Files => Set<FileEntity>();

    public DbSet<FileAuditEntity> FileAudits => Set<FileAuditEntity>();

    public DbSet<FileLinkAuditEntity> FileLinkAudits => Set<FileLinkAuditEntity>();

    public DbSet<FileSystemAuditEntity> FileSystemAudits => Set<FileSystemAuditEntity>();

    public DbSet<SearchHistoryEntryEntity> SearchHistory => Set<SearchHistoryEntryEntity>();

    public DbSet<SearchFavoriteEntity> SearchFavorites => Set<SearchFavoriteEntity>();

    public DbSet<SuggestionEntry> Suggestions => Set<SuggestionEntry>();

    public DbSet<DocumentLocationEntity> DocumentLocations => Set<DocumentLocationEntity>();

    public DbSet<DomainEventLogEntry> DomainEventLog => Set<DomainEventLogEntry>();

    public DbSet<ReindexQueueEntry> ReindexQueue => Set<ReindexQueueEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        using (InfrastructureModel.UseOptions(_options))
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        }

        modelBuilder.Entity<FtsWriteAheadRecord>(entity =>
        {
            entity.ToTable("fts_write_ahead");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.FileId).HasColumnName("file_id").HasColumnType("TEXT").IsRequired();
            entity.Property(e => e.Operation).HasColumnName("op").HasColumnType("TEXT").IsRequired();
            entity.Property(e => e.ContentHash).HasColumnName("content_hash").HasColumnType("TEXT");
            entity.Property(e => e.TitleHash).HasColumnName("title_hash").HasColumnType("TEXT");
            entity.Property(e => e.EnqueuedUtc).HasColumnName("enqueued_utc").HasColumnType("TEXT").IsRequired();
            entity.HasIndex(e => e.EnqueuedUtc).HasDatabaseName("idx_fts_write_ahead_enqueued");
        });

        modelBuilder.Entity<FtsWriteAheadDeadLetterRecord>(entity =>
        {
            entity.ToTable("fts_write_ahead_dlq");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.OriginalId).HasColumnName("original_id").HasColumnType("INTEGER").IsRequired();
            entity.Property(e => e.FileId).HasColumnName("file_id").HasColumnType("TEXT").IsRequired();
            entity.Property(e => e.Operation).HasColumnName("op").HasColumnType("TEXT").IsRequired();
            entity.Property(e => e.ContentHash).HasColumnName("content_hash").HasColumnType("TEXT");
            entity.Property(e => e.TitleHash).HasColumnName("title_hash").HasColumnType("TEXT");
            entity.Property(e => e.EnqueuedUtc).HasColumnName("enqueued_utc").HasColumnType("TEXT").IsRequired();
            entity.Property(e => e.DeadLetteredUtc).HasColumnName("dead_lettered_utc").HasColumnType("TEXT").IsRequired();
            entity.Property(e => e.Error).HasColumnName("error").HasColumnType("TEXT").IsRequired();
            entity.HasIndex(e => e.DeadLetteredUtc).HasDatabaseName("idx_fts_write_ahead_dlq_dead_lettered");
        });

    }

    /// <summary>
    /// Runs post-migration maintenance tasks such as PRAGMA optimize.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureSqliteProvider();
        await Database.ExecuteSqlRawAsync("PRAGMA optimize;", cancellationToken).ConfigureAwait(false);
    }

    internal async Task EnsureSqliteMigrationsLockClearedAsync(CancellationToken cancellationToken)
    {
        EnsureSqliteProvider();
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
        EnsureSqliteProvider();

        var connection = (SqliteConnection)Database.GetDbConnection();
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
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    internal async Task EnsureSqliteMigrationsHistoryBaselinedAsync(CancellationToken cancellationToken)
    {
        EnsureSqliteProvider();

        const string createTableSql = "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\n    \"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY,\n    \"ProductVersion\" TEXT NOT NULL\n);";
        var insertBaselineSql = $"INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('{LegacyBaselineMigrationId}', '9.0.9');";

        await Database.ExecuteSqlRawAsync(createTableSql, cancellationToken).ConfigureAwait(false);
        var inserted = await Database.ExecuteSqlRawAsync(insertBaselineSql, cancellationToken).ConfigureAwait(false);
        if (inserted > 0)
        {
            _logger.LogInformation("Baselined EF migrations history with initial migration for legacy SQLite database.");
        }
    }

    private void EnsureSqliteProvider()
    {
        var providerName = Database.ProviderName;
        if (string.IsNullOrWhiteSpace(providerName) || !providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("AppDbContext requires Microsoft.Data.Sqlite provider.");
        }
    }
}
