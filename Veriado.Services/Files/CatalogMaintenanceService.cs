using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Services.Files;

public sealed class CatalogMaintenanceService : ICatalogMaintenanceService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IFilePathResolver _pathResolver;
    private readonly ILogger<CatalogMaintenanceService> _logger;

    public CatalogMaintenanceService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IFilePathResolver pathResolver,
        ILogger<CatalogMaintenanceService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ClearCatalogAsync(CancellationToken cancellationToken = default)
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

        await db.Database.ExecuteSqlRawAsync("DELETE FROM search_document_fts;", cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM search_document;", cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
