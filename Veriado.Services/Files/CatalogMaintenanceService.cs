using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Infrastructure.Search;

namespace Veriado.Services.Files;

public sealed class CatalogMaintenanceService : ICatalogMaintenanceService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IFilePathResolver _pathResolver;
    private readonly IFulltextIntegrityService _fulltextIntegrityService;
    private readonly ILogger<CatalogMaintenanceService> _logger;
    private readonly InfrastructureOptions _options;

    public CatalogMaintenanceService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IFilePathResolver pathResolver,
        IFulltextIntegrityService fulltextIntegrityService,
        ILogger<CatalogMaintenanceService> logger,
        InfrastructureOptions options)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _fulltextIntegrityService = fulltextIntegrityService
            ?? throw new ArgumentNullException(nameof(fulltextIntegrityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task ClearCatalogAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await ClearCatalogInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.IndicatesDatabaseCorruption())
        {
            _logger.LogWarning(ex, "Database corruption detected while clearing catalog; recreating database file.");
            await RecreateDatabaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected failure while clearing catalog; recreating database file.");
            await RecreateDatabaseAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ClearCatalogInternalAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var fileSystems = await db.FileSystems.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var fs in fileSystems)
        {
            try
            {
                var fullPath = _pathResolver.GetFullPath(fs.RelativePath.Value);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete file {FileSystemId}", fs.Id);
            }
        }

        db.FileContentLinks.RemoveRange(db.FileContentLinks);
        db.FileAudits.RemoveRange(db.FileAudits);
        db.FileSystemAudits.RemoveRange(db.FileSystemAudits);
        db.Files.RemoveRange(db.Files);
        db.FileSystems.RemoveRange(db.FileSystems);

        db.SearchHistory.RemoveRange(db.SearchHistory);
        db.SearchFavorites.RemoveRange(db.SearchFavorites);
        db.Suggestions.RemoveRange(db.Suggestions);
        db.DocumentLocations.RemoveRange(db.DocumentLocations);
        db.ReindexQueue.RemoveRange(db.ReindexQueue);
        db.DomainEventLog.RemoveRange(db.DomainEventLog);

        var rebuildFulltext = false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("INSERT INTO search_document_fts(search_document_fts) VALUES ('delete-all');", cancellationToken)
                .ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM search_document;", cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (!ex.IndicatesFatalFulltextFailure())
        {
            rebuildFulltext = true;
            _logger.LogWarning(ex, "Failed to clear full-text catalog; forcing full rebuild.");
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (rebuildFulltext)
        {
            await _fulltextIntegrityService.RepairAsync(reindexAll: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RecreateDatabaseAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.DbPath))
        {
            _logger.LogWarning("Cannot recreate database because DbPath is not configured.");
            return;
        }

        try
        {
            if (File.Exists(_options.DbPath))
            {
                File.Delete(_options.DbPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete corrupted database file at {DbPath}.", _options.DbPath);
            throw;
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await db.InitializeAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Recreated SQLite database at {DbPath} after corruption was detected.", _options.DbPath);
    }
}
