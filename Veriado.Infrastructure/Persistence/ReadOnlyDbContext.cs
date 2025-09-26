using Microsoft.EntityFrameworkCore;
using Veriado.Domain.Audit;
using Veriado.Domain.Files;
using Veriado.Domain.Search;
using Veriado.Infrastructure.Persistence.Configurations;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Infrastructure.Search.Outbox;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Represents the pooled read-only context used for query workloads.
/// </summary>
public sealed class ReadOnlyDbContext : DbContext
{
    private readonly InfrastructureOptions _options;

    public ReadOnlyDbContext(DbContextOptions<ReadOnlyDbContext> options, InfrastructureOptions infrastructureOptions)
        : base(options)
    {
        _options = infrastructureOptions;
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        ChangeTracker.AutoDetectChangesEnabled = false;
        Database.SetCommandTimeout(30);
    }

    public DbSet<FileEntity> Files => Set<FileEntity>();

    public DbSet<FileAuditEntity> FileAudits => Set<FileAuditEntity>();

    public DbSet<FileContentAuditEntity> FileContentAudits => Set<FileContentAuditEntity>();

    public DbSet<FileDocumentValidityAuditEntity> FileValidityAudits => Set<FileDocumentValidityAuditEntity>();

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    public DbSet<SearchHistoryEntryEntity> SearchHistory => Set<SearchHistoryEntryEntity>();

    public DbSet<SearchFavoriteEntity> SearchFavorites => Set<SearchFavoriteEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        using (InfrastructureModel.UseOptions(_options))
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        }
    }
}
