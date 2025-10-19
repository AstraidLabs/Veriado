using System;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Persistence.Audit;
using Veriado.Infrastructure.Persistence.Configurations;
using Veriado.Infrastructure.Persistence.Entities;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Represents the pooled read-only context used for query workloads.
/// </summary>
public sealed class ReadOnlyDbContext : DbContext
{
    private readonly InfrastructureOptions _options;
    private readonly ILogger<ReadOnlyDbContext> _logger;

    public ReadOnlyDbContext(
        DbContextOptions<ReadOnlyDbContext> options,
        InfrastructureOptions infrastructureOptions,
        ILogger<ReadOnlyDbContext> logger)
        : base(options)
    {
        _options = infrastructureOptions;
        _logger = logger;
        EnsureSqliteProvider();
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        ChangeTracker.AutoDetectChangesEnabled = false;
        Database.SetCommandTimeout(30);
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        using (InfrastructureModel.UseOptions(_options))
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        }
    }

    private void EnsureSqliteProvider()
    {
        var providerName = Database.ProviderName;
        if (string.IsNullOrWhiteSpace(providerName) || !providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ReadOnlyDbContext requires Microsoft.Data.Sqlite provider.");
        }
    }
}
