using System;
using System.Data;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Veriado.Infrastructure.Persistence.Audit;
using Veriado.Infrastructure.Persistence.Configurations;
using Veriado.Infrastructure.Persistence.Entities;
using Veriado.Infrastructure.Persistence.EventLog;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Represents the primary EF Core context used for write operations.
/// </summary>
public sealed class AppDbContext : DbContext
{
    private readonly InfrastructureOptions _options;
    private readonly ILogger<AppDbContext> _logger;
    private const string LegacyBaselineMigrationId = "20251026112230_InitialCreate";
    private SemaphoreSlim? _saveChangesSemaphore = new(1, 1);

    public AppDbContext(DbContextOptions<AppDbContext> options, InfrastructureOptions infrastructureOptions, ILogger<AppDbContext> logger)
        : base(options)
    {
        _options = infrastructureOptions;
        _logger = logger;
        EnsureSqliteProvider();
        EnsureSaveChangesSemaphoreInitialized();
        _logger.LogInformation("ResolvedDbPath = {DatabasePath}", _options.DbPath);
    }

    public DbSet<FileEntity> Files => Set<FileEntity>();

    public DbSet<FileSystemEntity> FileSystems => Set<FileSystemEntity>();

    public DbSet<FileContentLinkRow> FileContentLinks => Set<FileContentLinkRow>();

    public DbSet<FileAuditRecord> FileAudits => Set<FileAuditRecord>();

    public DbSet<FileLinkAuditRecord> FileLinkAudits => Set<FileLinkAuditRecord>();

    public DbSet<FileSystemAuditRecord> FileSystemAudits => Set<FileSystemAuditRecord>();

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

    internal bool IsSaveChangesSemaphoreDisposed
        => Volatile.Read(ref _saveChangesSemaphore) is null;

    internal SemaphoreSlim SaveChangesSemaphore
    {
        get
        {
            var semaphore = Volatile.Read(ref _saveChangesSemaphore);
            if (semaphore is null)
            {
                throw new ObjectDisposedException(nameof(AppDbContext));
            }

            return semaphore;
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        EnsureSaveChangesSemaphoreInitialized();
    }

    public override int SaveChanges()
    {
        throw new NotSupportedException("Synchronous SaveChanges is not supported. Use SaveChangesAsync instead.");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        throw new NotSupportedException("Synchronous SaveChanges is not supported. Use SaveChangesAsync instead.");
    }

    public override void Dispose()
    {
        DisposeSemaphore();
        base.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        DisposeSemaphore();
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private void EnsureSaveChangesSemaphoreInitialized()
    {
        if (Volatile.Read(ref _saveChangesSemaphore) is not null)
        {
            return;
        }

        var semaphore = new SemaphoreSlim(1, 1);
        var existing = Interlocked.CompareExchange(ref _saveChangesSemaphore, semaphore, null);
        if (existing is not null)
        {
            semaphore.Dispose();
        }
    }

    private void DisposeSemaphore()
    {
        var semaphore = Interlocked.Exchange(ref _saveChangesSemaphore, null);
        semaphore?.Dispose();
    }
}
