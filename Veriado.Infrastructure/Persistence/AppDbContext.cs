using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Veriado.Domain.Audit;
using Veriado.Domain.Files;
using Veriado.Infrastructure.MetadataStore.Kv;
using Veriado.Domain.Search;
using Veriado.Infrastructure.Persistence.Configurations;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Infrastructure.Search.Outbox;

namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Represents the primary EF Core context used for write operations.
/// </summary>
public sealed class AppDbContext : DbContext
{
    private readonly InfrastructureOptions _options;

    public AppDbContext(DbContextOptions<AppDbContext> options, InfrastructureOptions infrastructureOptions)
        : base(options)
    {
        _options = infrastructureOptions;
    }

    public DbSet<FileEntity> Files => Set<FileEntity>();

    public DbSet<FileAuditEntity> FileAudits => Set<FileAuditEntity>();

    public DbSet<FileContentAuditEntity> FileContentAudits => Set<FileContentAuditEntity>();

    public DbSet<FileDocumentValidityAuditEntity> FileValidityAudits => Set<FileDocumentValidityAuditEntity>();

    public DbSet<ExtMetadataEntry> ExtendedMetadataEntries => Set<ExtMetadataEntry>();

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    public DbSet<SearchHistoryEntryEntity> SearchHistory => Set<SearchHistoryEntryEntity>();

    public DbSet<SearchFavoriteEntity> SearchFavorites => Set<SearchFavoriteEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        using (InfrastructureModel.UseOptions(_options))
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        }

        if (!_options.UseKvMetadata)
        {
            modelBuilder.Entity<ExtMetadataEntry>().ToTable("file_ext_metadata").Metadata.SetIsTableExcludedFromMigrations(true);
        }

        if (_options.FtsIndexingMode != FtsIndexingMode.Outbox)
        {
            modelBuilder.Entity<OutboxEvent>().ToTable("outbox_events").Metadata.SetIsTableExcludedFromMigrations(true);
        }
    }

    /// <summary>
    /// Applies database migrations and runs PRAGMA optimize.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await Database.ExecuteSqlRawAsync("PRAGMA optimize;", cancellationToken).ConfigureAwait(false);
    }
}
